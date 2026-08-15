use crate::languages;
use crate::symbols::{self, ExtractedSymbol};
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

pub struct IndexedFile {
    pub absolute_path: String,
    pub relative_path: String,
    pub extension: Option<String>,
    pub language: Option<&'static str>,
    pub size: i64,
    pub mtime: i64,
    pub symbols: Vec<ExtractedSymbol>,
}

pub fn scan(root: &Path) -> Result<Vec<IndexedFile>, String> {
    let configured = std::env::var("SHIORI_EXCLUDE_PATTERNS").unwrap_or_default();
    let patterns = configured
        .split(';')
        .map(str::trim)
        .filter(|pattern| !pattern.is_empty())
        .collect::<Vec<_>>();
    scan_with_patterns(root, &patterns)
}

fn scan_with_patterns(root: &Path, patterns: &[&str]) -> Result<Vec<IndexedFile>, String> {
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

    let mut files = Vec::new();
    for entry in builder.build() {
        let entry = entry.map_err(|source| format!("cannot scan workspace: {source}"))?;
        let file_type = match entry.file_type() {
            Some(value) if value.is_file() => value,
            _ => continue,
        };
        let _ = file_type;
        let path = entry.path();
        let relative = path
            .strip_prefix(root)
            .map_err(|source| format!("cannot resolve indexed path: {source}"))?;
        let metadata = entry
            .metadata()
            .map_err(|source| format!("cannot read file metadata: {source}"))?;
        let extension = path
            .extension()
            .and_then(|value| value.to_str())
            .map(str::to_lowercase);
        let language = languages::detect(path);
        let symbols = if let Some(language) = language {
            let source = std::fs::read(path).map_err(|error| {
                format!("cannot read source file '{}': {error}", path.display())
            })?;
            let tree = languages::parse(language, &source).map_err(|error| {
                format!("cannot parse source file '{}': {error}", path.display())
            })?;
            symbols::extract(language, &source, &tree)
        } else {
            Vec::new()
        };
        files.push(IndexedFile {
            absolute_path: normalize_path(path),
            relative_path: normalize_path(relative),
            language: language.map(|value| value.name()),
            extension,
            size: i64::try_from(metadata.len()).unwrap_or(i64::MAX),
            mtime: modified_time(&metadata),
            symbols,
        });
    }
    files.sort_unstable_by(|left, right| left.relative_path.cmp(&right.relative_path));
    Ok(files)
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

fn modified_time(metadata: &std::fs::Metadata) -> i64 {
    metadata
        .modified()
        .ok()
        .and_then(|value| value.duration_since(UNIX_EPOCH).ok())
        .and_then(|value| i64::try_from(value.as_secs()).ok())
        .unwrap_or_default()
}

fn normalize_path(path: &Path) -> String {
    path.to_string_lossy().replace('\\', "/")
}

#[cfg(test)]
mod tests {
    use super::{scan, scan_with_patterns};
    use std::fs;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn scan_respects_gitignore_and_default_exclusions() {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system time should be valid")
            .as_nanos();
        let root =
            std::env::temp_dir().join(format!("shiori-index-test-{}-{unique}", std::process::id()));
        fs::create_dir_all(root.join("src")).expect("source directory should be created");
        fs::create_dir_all(root.join("target")).expect("excluded directory should be created");
        fs::write(root.join(".gitignore"), "ignored.rs\n").expect("gitignore should be written");
        fs::write(root.join("src").join("main.rs"), "fn main() {}")
            .expect("source file should be written");
        fs::write(root.join("ignored.rs"), "ignored").expect("ignored file should be written");
        fs::write(root.join("target").join("output.bin"), "ignored")
            .expect("excluded file should be written");

        let files = scan(&root).expect("workspace should be scanned");

        assert!(files.iter().any(|file| file.relative_path == "src/main.rs"));
        assert!(files.iter().any(|file| file.relative_path == ".gitignore"));
        assert!(!files.iter().any(|file| file.relative_path == "ignored.rs"));
        assert!(
            !files
                .iter()
                .any(|file| file.relative_path.contains("target"))
        );
        fs::remove_dir_all(root).expect("test directory should be removed");
    }

    #[test]
    fn scan_respects_configured_exclusion_patterns() {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system time should be valid")
            .as_nanos();
        let root = std::env::temp_dir().join(format!(
            "shiori-custom-exclude-test-{}-{unique}",
            std::process::id()
        ));
        fs::create_dir_all(root.join("generated")).expect("directory should be created");
        fs::write(root.join("keep.rs"), "keep").expect("source file should be written");
        fs::write(root.join("generated").join("skip.rs"), "skip")
            .expect("excluded file should be written");

        let files =
            scan_with_patterns(&root, &["generated/**"]).expect("workspace should be scanned");

        assert!(files.iter().any(|file| file.relative_path == "keep.rs"));
        assert!(
            !files
                .iter()
                .any(|file| file.relative_path == "generated/skip.rs")
        );
        fs::remove_dir_all(root).expect("test directory should be removed");
    }
}
