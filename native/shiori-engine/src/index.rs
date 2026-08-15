use crate::languages;
use crate::symbols::{self, ExtractedSymbol};
use ignore::overrides::OverrideBuilder;
use ignore::{DirEntry, WalkBuilder};
use sha2::{Digest, Sha256};
use std::collections::HashMap;
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
    pub content_hash: String,
    pub symbols: Vec<ExtractedSymbol>,
}

#[derive(Clone)]
pub struct FileFingerprint {
    pub size: i64,
    pub mtime: i64,
    pub content_hash: Option<String>,
}

pub struct MetadataUpdate {
    pub relative_path: String,
    pub absolute_path: String,
    pub size: i64,
    pub mtime: i64,
}

pub struct IncrementalScan {
    pub upserts: Vec<IndexedFile>,
    pub metadata_updates: Vec<MetadataUpdate>,
    pub current_paths: Vec<String>,
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
    let entries = walk(root, patterns)?;
    entries
        .into_iter()
        .map(|entry| {
            index_file(
                &entry.path,
                &entry.relative_path,
                entry.size,
                entry.mtime,
                None,
            )
        })
        .collect()
}

pub fn scan_incremental(
    root: &Path,
    previous: &HashMap<String, FileFingerprint>,
) -> Result<IncrementalScan, String> {
    let configured = std::env::var("SHIORI_EXCLUDE_PATTERNS").unwrap_or_default();
    let patterns = configured
        .split(';')
        .map(str::trim)
        .filter(|pattern| !pattern.is_empty())
        .collect::<Vec<_>>();
    let entries = walk(root, &patterns)?;
    let mut scan = IncrementalScan {
        upserts: Vec::new(),
        metadata_updates: Vec::new(),
        current_paths: Vec::with_capacity(entries.len()),
    };
    for entry in entries {
        scan.current_paths.push(entry.relative_path.clone());
        let Some(previous) = previous.get(&entry.relative_path) else {
            scan.upserts.push(index_file(
                &entry.path,
                &entry.relative_path,
                entry.size,
                entry.mtime,
                None,
            )?);
            continue;
        };
        if previous.size == entry.size
            && previous.mtime == entry.mtime
            && previous.content_hash.is_some()
        {
            continue;
        }
        let source = std::fs::read(&entry.path).map_err(|error| {
            format!(
                "cannot read source file '{}': {error}",
                entry.path.display()
            )
        })?;
        let content_hash = content_hash(&source);
        if previous.content_hash.as_deref() == Some(&content_hash) {
            scan.metadata_updates.push(MetadataUpdate {
                relative_path: entry.relative_path,
                absolute_path: normalize_path(&entry.path),
                size: entry.size,
                mtime: entry.mtime,
            });
        } else {
            scan.upserts.push(index_file(
                &entry.path,
                &entry.relative_path,
                entry.size,
                entry.mtime,
                Some(source),
            )?);
        }
    }
    Ok(scan)
}

struct WalkedFile {
    path: std::path::PathBuf,
    relative_path: String,
    size: i64,
    mtime: i64,
}

fn walk(root: &Path, patterns: &[&str]) -> Result<Vec<WalkedFile>, String> {
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
        files.push(WalkedFile {
            path: path.to_path_buf(),
            relative_path: normalize_path(relative),
            size: i64::try_from(metadata.len()).unwrap_or(i64::MAX),
            mtime: modified_time(&metadata),
        });
    }
    files.sort_unstable_by(|left, right| left.relative_path.cmp(&right.relative_path));
    Ok(files)
}

fn index_file(
    path: &Path,
    relative_path: &str,
    size: i64,
    mtime: i64,
    source: Option<Vec<u8>>,
) -> Result<IndexedFile, String> {
    let source = match source {
        Some(value) => value,
        None => std::fs::read(path)
            .map_err(|error| format!("cannot read source file '{}': {error}", path.display()))?,
    };
    let language = languages::detect(path);
    let symbols = if let Some(language) = language {
        let tree = languages::parse(language, &source)
            .map_err(|error| format!("cannot parse source file '{}': {error}", path.display()))?;
        symbols::extract(language, &source, &tree)
    } else {
        Vec::new()
    };
    Ok(IndexedFile {
        absolute_path: normalize_path(path),
        relative_path: relative_path.to_owned(),
        extension: path
            .extension()
            .and_then(|value| value.to_str())
            .map(str::to_lowercase),
        language: language.map(|value| value.name()),
        size,
        mtime,
        content_hash: content_hash(&source),
        symbols,
    })
}

fn content_hash(source: &[u8]) -> String {
    Sha256::digest(source)
        .iter()
        .fold(String::with_capacity(64), |mut output, byte| {
            use std::fmt::Write;
            write!(output, "{byte:02x}").expect("writing to String cannot fail");
            output
        })
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
        .and_then(|value| i64::try_from(value.as_nanos()).ok())
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
