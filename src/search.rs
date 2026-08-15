use crate::workspace::Workspace;
use std::ffi::OsStr;
use std::fmt;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;

const DEFAULT_EXCLUSIONS: &[&str] = &[
    ".git", "node_modules", "bin", "obj", "target", "dist", "build", ".vs", ".idea",
    ".next", "coverage", "vendor", "packages",
];

/// A code location returned by a search provider.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SearchResult {
    pub path: PathBuf,
    pub line: Option<u64>,
    pub snippet: Option<String>,
}

/// Searches file names and relative paths without leaving the workspace.
pub fn search_files(
    workspace: &Workspace,
    query: &str,
    limit: usize,
) -> Result<Vec<SearchResult>, SearchError> {
    if query.is_empty() {
        return Err(SearchError::InvalidQuery("query must not be empty"));
    }

    let mut results = Vec::new();
    walk(workspace.root(), workspace.root(), query, limit, &mut results)?;
    Ok(results)
}

fn walk(
    root: &Path,
    directory: &Path,
    query: &str,
    limit: usize,
    results: &mut Vec<SearchResult>,
) -> Result<(), SearchError> {
    if results.len() >= limit {
        return Ok(());
    }

    let mut entries = fs::read_dir(directory)
        .map_err(SearchError::Io)?
        .collect::<Result<Vec<_>, _>>()
        .map_err(SearchError::Io)?;
    entries.sort_by_key(|entry| entry.file_name());

    for entry in entries {
        if results.len() >= limit {
            break;
        }

        let path = entry.path();
        let file_type = entry.file_type().map_err(SearchError::Io)?;
        if file_type.is_symlink() {
            continue;
        }
        if file_type.is_dir() {
            if !is_excluded(entry.file_name()) {
                walk(root, &path, query, limit, results)?;
            }
            continue;
        }

        let relative = path.strip_prefix(root).unwrap_or(&path);
        if relative
            .to_string_lossy()
            .to_lowercase()
            .contains(&query.to_lowercase())
        {
            results.push(SearchResult {
                path: relative.to_path_buf(),
                line: None,
                snippet: None,
            });
        }
    }

    Ok(())
}

fn is_excluded(name: impl AsRef<OsStr>) -> bool {
    let name = name.as_ref().to_string_lossy();
    DEFAULT_EXCLUSIONS.iter().any(|excluded| name == *excluded)
}

/// Uses ripgrep for bounded text search within the workspace.
pub fn search_text(
    workspace: &Workspace,
    query: &str,
    limit: usize,
) -> Result<Vec<SearchResult>, SearchError> {
    if query.is_empty() {
        return Err(SearchError::InvalidQuery("query must not be empty"));
    }

    let output = Command::new("rg")
        .arg("--line-number")
        .arg("--no-heading")
        .arg("--color=never")
        .arg("--fixed-strings")
        .arg("--")
        .arg(query)
        .arg(".")
        .current_dir(workspace.root())
        .output()
        .map_err(SearchError::RipgrepUnavailable)?;

    if !output.status.success() && output.status.code() != Some(1) {
        return Err(SearchError::RipgrepFailed(
            String::from_utf8_lossy(&output.stderr).trim().to_owned(),
        ));
    }

    Ok(String::from_utf8_lossy(&output.stdout)
        .lines()
        .take(limit)
        .filter_map(parse_ripgrep_line)
        .collect())
}

fn parse_ripgrep_line(line: &str) -> Option<SearchResult> {
    let mut parts = line.splitn(3, ':');
    let path = PathBuf::from(parts.next()?);
    let line_number = parts.next()?.parse().ok()?;
    let snippet = parts.next()?.to_owned();
    Some(SearchResult {
        path,
        line: Some(line_number),
        snippet: Some(snippet),
    })
}

/// Error returned by search operations.
#[derive(Debug)]
pub enum SearchError {
    InvalidQuery(&'static str),
    Io(std::io::Error),
    RipgrepUnavailable(std::io::Error),
    RipgrepFailed(String),
}

impl fmt::Display for SearchError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::InvalidQuery(message) => formatter.write_str(message),
            Self::Io(source) => write!(formatter, "file search failed: {source}"),
            Self::RipgrepUnavailable(source) => write!(formatter, "ripgrep is unavailable: {source}"),
            Self::RipgrepFailed(message) => write!(formatter, "ripgrep failed: {message}"),
        }
    }
}

impl std::error::Error for SearchError {}

#[cfg(test)]
mod tests {
    use super::parse_ripgrep_line;
    use std::path::PathBuf;

    #[test]
    fn parse_ripgrep_line_when_valid_returns_structured_result() {
        let result = parse_ripgrep_line("src/main.rs:42:hello").expect("line should parse");

        assert_eq!(result.path, PathBuf::from("src/main.rs"));
        assert_eq!(result.line, Some(42));
        assert_eq!(result.snippet.as_deref(), Some("hello"));
    }
}
