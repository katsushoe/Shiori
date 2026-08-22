use ignore::overrides::OverrideBuilder;
use ignore::{DirEntry, WalkBuilder};
use std::path::Path;
use std::time::UNIX_EPOCH;

pub const DEFAULT_EXCLUSIONS: &[&str] = &[
    ".git",
    "node_modules",
    "bin",
    "obj",
    "target",
    "dist",
    "build",
    ".vs",
    ".idea",
    ".next",
    "coverage",
    "vendor",
    "packages",
];

#[derive(Clone)]
pub struct IndexedFile {
    pub absolute_path: String,
    pub relative_path: String,
    pub file_name: String,
    pub extension: Option<String>,
    pub size: i64,
    pub mtime: i64,
}

pub enum ScanEvent {
    File(IndexedFile),
    DirectoryComplete(String),
}

impl IndexedFile {
    pub fn estimated_bytes(&self) -> usize {
        self.absolute_path.len()
            + self.relative_path.len()
            + self.file_name.len()
            + self.extension.as_ref().map_or(0, String::len)
            + std::mem::size_of::<Self>()
    }
}

pub fn count_directories(root: &Path) -> Result<u64, String> {
    let mut count = 0_u64;
    for entry in walker(root)?.build() {
        let entry = entry.map_err(|source| format!("cannot scan workspace: {source}"))?;
        if entry.file_type().is_some_and(|value| value.is_dir()) {
            count = count.saturating_add(1);
        }
    }
    Ok(count)
}

pub fn scan(
    root: &Path,
    mut on_event: impl FnMut(ScanEvent) -> Result<(), String>,
) -> Result<(), String> {
    let mut directories = Vec::<(usize, String)>::new();
    for entry in walker(root)?.build() {
        let entry = entry.map_err(|source| format!("cannot scan workspace: {source}"))?;
        let depth = entry.depth();
        complete_directories(&mut directories, depth, &mut on_event)?;
        let file_type = match entry.file_type() {
            Some(value) => value,
            None => continue,
        };
        if file_type.is_dir() {
            directories.push((depth, relative_path(root, entry.path())?));
            continue;
        }
        if !file_type.is_file() {
            continue;
        }
        let metadata = entry
            .metadata()
            .map_err(|source| format!("cannot read file metadata: {source}"))?;
        let relative_path = relative_path(root, entry.path())?;
        let file_name = entry.file_name().to_string_lossy().into_owned();
        on_event(ScanEvent::File(IndexedFile {
            absolute_path: normalize_path(entry.path()),
            relative_path,
            file_name,
            extension: entry
                .path()
                .extension()
                .and_then(|value| value.to_str())
                .map(str::to_lowercase),
            size: i64::try_from(metadata.len()).unwrap_or(i64::MAX),
            mtime: modified_time(&metadata),
        }))?;
    }
    while let Some((_, path)) = directories.pop() {
        on_event(ScanEvent::DirectoryComplete(path))?;
    }
    Ok(())
}

fn complete_directories(
    directories: &mut Vec<(usize, String)>,
    next_depth: usize,
    on_event: &mut impl FnMut(ScanEvent) -> Result<(), String>,
) -> Result<(), String> {
    while directories
        .last()
        .is_some_and(|(depth, _)| *depth >= next_depth)
    {
        let (_, path) = directories.pop().expect("directory stack is not empty");
        on_event(ScanEvent::DirectoryComplete(path))?;
    }
    Ok(())
}

fn walker(root: &Path) -> Result<WalkBuilder, String> {
    let mut builder = WalkBuilder::new(root);
    builder
        .follow_links(false)
        .hidden(false)
        .git_ignore(true)
        .git_global(true)
        .git_exclude(true)
        .require_git(false)
        .parents(true)
        .filter_entry(|entry| !is_excluded(entry));

    let configured = std::env::var("SHIORI_EXCLUDE_PATTERNS").unwrap_or_default();
    let patterns = configured
        .split(';')
        .map(str::trim)
        .filter(|pattern| !pattern.is_empty())
        .collect::<Vec<_>>();
    if !patterns.is_empty() {
        let mut overrides = OverrideBuilder::new(root);
        for pattern in patterns {
            overrides
                .add(&format!("!{pattern}"))
                .map_err(|source| format!("invalid exclusion pattern '{pattern}': {source}"))?;
        }
        builder.overrides(
            overrides
                .build()
                .map_err(|source| format!("invalid exclusion patterns: {source}"))?,
        );
    }
    Ok(builder)
}

fn is_excluded(entry: &DirEntry) -> bool {
    entry.depth() > 0
        && DEFAULT_EXCLUSIONS.iter().any(|excluded| {
            entry
                .file_name()
                .to_string_lossy()
                .eq_ignore_ascii_case(excluded)
        })
}

fn relative_path(root: &Path, path: &Path) -> Result<String, String> {
    let relative = path
        .strip_prefix(root)
        .map_err(|source| format!("cannot resolve indexed path: {source}"))?;
    if relative.as_os_str().is_empty() {
        return Ok(".".to_owned());
    }
    Ok(normalize_path(relative))
}

fn modified_time(metadata: &std::fs::Metadata) -> i64 {
    metadata
        .modified()
        .ok()
        .and_then(|value| value.duration_since(UNIX_EPOCH).ok())
        .and_then(|value| i64::try_from(value.as_nanos()).ok())
        .unwrap_or_default()
}

fn normalize_path(path: &Path) -> String {
    path.to_string_lossy().replace('\\', "/")
}

#[cfg(test)]
mod tests {
    use super::{ScanEvent, count_directories, scan};
    use std::fs;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn scan_respects_exclusions_and_reports_directories() {
        let root = temporary_root("scan");
        fs::create_dir_all(root.join("src")).expect("source directory should be created");
        fs::create_dir_all(root.join("target")).expect("excluded directory should be created");
        fs::write(root.join("src").join("main.rs"), "fn main() {}")
            .expect("source file should be written");
        fs::write(root.join("target").join("output.bin"), "ignored")
            .expect("excluded file should be written");

        let count = count_directories(&root).expect("directories should be counted");
        let mut files = Vec::new();
        let mut directories = Vec::new();
        scan(&root, |event| {
            match event {
                ScanEvent::File(file) => files.push(file.relative_path),
                ScanEvent::DirectoryComplete(path) => directories.push(path),
            }
            Ok(())
        })
        .expect("workspace should be scanned");

        assert_eq!(count, 2);
        assert_eq!(directories.len(), 2);
        assert_eq!(files, ["src/main.rs"]);
        fs::remove_dir_all(root).expect("test directory should be removed");
    }

    fn temporary_root(label: &str) -> std::path::PathBuf {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system time should be valid")
            .as_nanos();
        std::env::temp_dir().join(format!(
            "shiori-index-{label}-{}-{unique}",
            std::process::id()
        ))
    }
}
