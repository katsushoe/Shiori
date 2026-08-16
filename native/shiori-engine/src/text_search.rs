use crate::index::DEFAULT_EXCLUSIONS;
use globset::{Glob, GlobMatcher};
use serde::{Deserialize, Serialize};
use std::io::{BufRead, BufReader, Read};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

#[derive(Deserialize)]
pub struct TextSearchRequest {
    pub query: String,
    pub path: Option<String>,
    pub glob: Option<String>,
    pub regex: bool,
    pub case_sensitive: bool,
    pub context_lines: usize,
    pub limit: usize,
}

#[derive(Debug, Serialize)]
pub struct TextSearchResponse {
    pub results: Vec<TextSearchResult>,
}

#[derive(Debug, Serialize)]
pub struct TextSearchResult {
    #[serde(rename = "type")]
    pub result_type: &'static str,
    pub path: String,
    pub line: u64,
    pub column: u64,
    pub snippet: String,
}

#[derive(Deserialize)]
struct RipgrepMessage {
    #[serde(rename = "type")]
    message_type: String,
    data: RipgrepData,
}

#[derive(Deserialize)]
struct RipgrepData {
    path: Option<RipgrepText>,
    lines: Option<RipgrepText>,
    line_number: Option<u64>,
    #[serde(default)]
    submatches: Vec<RipgrepSubmatch>,
}

#[derive(Deserialize)]
struct RipgrepText {
    text: Option<String>,
}

#[derive(Deserialize)]
struct RipgrepSubmatch {
    start: u64,
}

pub fn search(root: &Path, request: &TextSearchRequest) -> Result<TextSearchResponse, String> {
    validate(request)?;
    let glob = request
        .glob
        .as_deref()
        .filter(|value| !value.is_empty())
        .map(|pattern| {
            Glob::new(pattern)
                .map(|glob| glob.compile_matcher())
                .map_err(|source| format!("invalid glob '{pattern}': {source}"))
        })
        .transpose()?;
    let root = root
        .canonicalize()
        .map_err(|source| format!("workspace is unavailable: {source}"))?;
    let target = resolve_target(&root, request.path.as_deref())?;
    let relative_target = target
        .strip_prefix(&root)
        .map_err(|source| format!("search path is outside the workspace: {source}"))?;
    let search_path = if relative_target.as_os_str().is_empty() {
        Path::new(".")
    } else {
        relative_target
    };

    let mut command = Command::new("rg");
    command
        .current_dir(&root)
        .arg("--json")
        .arg("--color=never")
        .arg("--no-heading")
        .arg("--with-filename")
        .arg("--hidden")
        .arg("--no-require-git")
        .arg(if request.case_sensitive {
            "--case-sensitive"
        } else {
            "--ignore-case"
        });
    if !request.regex {
        command.arg("--fixed-strings");
    }
    for excluded in DEFAULT_EXCLUSIONS {
        command
            .arg("--glob")
            .arg(format!("!{excluded}/**"))
            .arg("--glob")
            .arg(format!("!**/{excluded}/**"));
    }
    if let Ok(configured) = std::env::var("SHIORI_EXCLUDE_PATTERNS") {
        for pattern in configured
            .split(';')
            .map(str::trim)
            .filter(|pattern| !pattern.is_empty())
        {
            command.arg("--glob").arg(format!("!{pattern}"));
        }
    }
    command
        .arg("--regexp")
        .arg(&request.query)
        .arg("--")
        .arg(search_path)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());

    let mut child = command
        .spawn()
        .map_err(|source| format!("cannot start ripgrep: {source}"))?;
    let stdout = child
        .stdout
        .take()
        .ok_or_else(|| "cannot read ripgrep output".to_owned())?;
    let mut results = Vec::new();
    let mut stopped_at_limit = false;
    for line in BufReader::new(stdout).lines() {
        let line = line.map_err(|source| format!("cannot read ripgrep output: {source}"))?;
        let message: RipgrepMessage = serde_json::from_str(&line)
            .map_err(|source| format!("cannot parse ripgrep output: {source}"))?;
        if message.message_type != "match" {
            continue;
        }
        if let Some(result) = convert_match(&root, message.data, request.context_lines)?
            && matches_glob(glob.as_ref(), &result.path)
        {
            results.push(result);
        }
        if results.len() >= request.limit {
            child
                .kill()
                .map_err(|source| format!("cannot stop ripgrep at result limit: {source}"))?;
            stopped_at_limit = true;
            break;
        }
    }

    let status = child
        .wait()
        .map_err(|source| format!("cannot wait for ripgrep: {source}"))?;
    if !stopped_at_limit && !status.success() && status.code() != Some(1) {
        let mut error = String::new();
        if let Some(mut stderr) = child.stderr.take() {
            stderr
                .read_to_string(&mut error)
                .map_err(|source| format!("cannot read ripgrep error: {source}"))?;
        }
        return Err(format!("ripgrep failed: {}", error.trim()));
    }
    Ok(TextSearchResponse { results })
}

fn matches_glob(glob: Option<&GlobMatcher>, path: &str) -> bool {
    match glob {
        Some(matcher) => matcher.is_match(path),
        None => true,
    }
}

fn validate(request: &TextSearchRequest) -> Result<(), String> {
    if request.query.is_empty() {
        return Err("query must not be empty".to_owned());
    }
    if !(1..=100).contains(&request.limit) {
        return Err("limit must be between 1 and 100".to_owned());
    }
    if request.context_lines > 10 {
        return Err("context_lines must be between 0 and 10".to_owned());
    }
    Ok(())
}

fn resolve_target(root: &Path, requested: Option<&str>) -> Result<PathBuf, String> {
    let candidate = match requested.filter(|value| !value.is_empty()) {
        Some(path) => root.join(path),
        None => root.to_path_buf(),
    };
    let target = candidate
        .canonicalize()
        .map_err(|source| format!("search path is unavailable: {source}"))?;
    if !target.starts_with(root) {
        return Err("search path is outside the workspace".to_owned());
    }
    Ok(target)
}

fn convert_match(
    root: &Path,
    data: RipgrepData,
    context_lines: usize,
) -> Result<Option<TextSearchResult>, String> {
    let path = match data.path.and_then(|value| value.text) {
        Some(value) => value,
        None => return Ok(None),
    };
    let line_number = match data.line_number {
        Some(value) => value,
        None => return Ok(None),
    };
    let matching_line = data
        .lines
        .and_then(|value| value.text)
        .unwrap_or_default()
        .trim_end_matches(['\r', '\n'])
        .to_owned();
    let column = data
        .submatches
        .first()
        .map(|value| value.start + 1)
        .unwrap_or(1);
    let normalized_path = path.replace('\\', "/");
    let display_path = normalized_path
        .strip_prefix("./")
        .unwrap_or(&normalized_path);
    let relative = Path::new(display_path);
    let snippet = if context_lines == 0 {
        matching_line
    } else {
        read_snippet(&root.join(relative), line_number, context_lines).unwrap_or(matching_line)
    };
    Ok(Some(TextSearchResult {
        result_type: "text",
        path: display_path.to_owned(),
        line: line_number,
        column,
        snippet,
    }))
}

fn read_snippet(path: &Path, line_number: u64, context_lines: usize) -> Option<String> {
    let content = std::fs::read_to_string(path).ok()?;
    let lines = content.lines().collect::<Vec<_>>();
    let line_index = usize::try_from(line_number).ok()?.checked_sub(1)?;
    let start = line_index.saturating_sub(context_lines);
    let end = line_index
        .saturating_add(context_lines)
        .saturating_add(1)
        .min(lines.len());
    Some(lines.get(start..end)?.join("\n"))
}

#[cfg(test)]
mod tests {
    use super::{TextSearchRequest, search};
    use std::fs;
    use std::path::Path;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn search_returns_bounded_structured_matches() {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system time should be valid")
            .as_nanos();
        let root = std::env::temp_dir().join(format!(
            "shiori-text-search-test-{}-{unique}",
            std::process::id()
        ));
        fs::create_dir_all(&root).expect("workspace should be created");
        fs::create_dir_all(root.join("target")).expect("excluded directory should be created");
        fs::write(
            root.join("sample.rs"),
            "before\nNeedle here\nafter\nNeedle again\n",
        )
        .expect("sample should be written");
        fs::write(root.join(".gitignore"), "ignored.rs\n").expect("gitignore should be written");
        fs::write(root.join("ignored.rs"), "Needle ignored\n")
            .expect("ignored file should be written");
        fs::write(root.join("target").join("output.rs"), "Needle excluded\n")
            .expect("excluded file should be written");
        let request = TextSearchRequest {
            query: "needle".to_owned(),
            path: None,
            glob: Some("*.rs".to_owned()),
            regex: false,
            case_sensitive: false,
            context_lines: 1,
            limit: 2,
        };

        let response = search(&root, &request).expect("text search should succeed");

        assert_eq!(response.results.len(), 2);
        assert!(
            response
                .results
                .iter()
                .all(|result| result.path == "sample.rs"),
            "unexpected results: {:?}",
            response.results
        );
        assert_eq!(response.results[0].line, 2);
        assert_eq!(response.results[0].column, 1);
        assert_eq!(response.results[0].snippet, "before\nNeedle here\nafter");
        fs::remove_dir_all(root).expect("test directory should be removed");
    }

    #[test]
    fn search_rejects_path_outside_workspace() {
        let request = TextSearchRequest {
            query: "needle".to_owned(),
            path: Some("..".to_owned()),
            glob: None,
            regex: false,
            case_sensitive: true,
            context_lines: 0,
            limit: 20,
        };

        let error = search(Path::new(env!("CARGO_MANIFEST_DIR")), &request)
            .expect_err("outside path should fail");

        assert_eq!(error, "search path is outside the workspace");
    }
}
