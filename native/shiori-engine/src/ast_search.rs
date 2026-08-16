use crate::index;
use crate::languages::{self, LanguageId};
use serde::{Deserialize, Serialize};
use std::path::Path;
use tree_sitter::{Query, QueryCursor, StreamingIterator};

#[derive(Deserialize)]
pub struct AstSearchRequest {
    pub language: String,
    pub pattern: String,
    pub path: Option<String>,
    pub limit: usize,
}

#[derive(Serialize)]
pub struct AstSearchResponse {
    pub results: Vec<AstSearchResult>,
}

#[derive(Serialize)]
pub struct AstSearchResult {
    pub path: String,
    pub line: usize,
    pub column: usize,
    pub capture: String,
    pub kind: String,
    pub snippet: String,
}

pub fn search(root: &Path, request: &AstSearchRequest) -> Result<AstSearchResponse, String> {
    if request.pattern.trim().is_empty() || !(1..=100).contains(&request.limit) {
        return Err("pattern must not be empty and limit must be from 1 to 100".to_owned());
    }
    let language = LanguageId::from_name(request.language.trim())
        .ok_or_else(|| format!("unsupported AST language: {}", request.language))?;
    let grammar = language.grammar();
    let query = Query::new(&grammar, request.pattern.trim())
        .map_err(|source| format!("invalid Tree-sitter query: {source}"))?;
    let path_filter = request
        .path
        .as_deref()
        .map(|value| value.trim().replace('\\', "/").trim_matches('/').to_owned())
        .filter(|value| !value.is_empty());
    if path_filter.as_ref().is_some_and(|filter| {
        Path::new(filter).is_absolute() || filter.split('/').any(|segment| segment == "..")
    }) {
        return Err("AST search path must be relative and stay within the workspace".to_owned());
    }
    let files = index::scan(root)?;
    let mut results = Vec::new();
    for file in files.into_iter().filter(|file| {
        file.language == Some(language.name())
            && path_filter.as_ref().is_none_or(|filter| {
                file.relative_path == *filter
                    || file
                        .relative_path
                        .strip_prefix(filter)
                        .is_some_and(|suffix| suffix.starts_with('/'))
            })
    }) {
        let source = std::fs::read(&file.absolute_path).map_err(|error| {
            format!("cannot read source file '{}': {error}", file.relative_path)
        })?;
        let tree = languages::parse(language, &source)?;
        let capture_names = query.capture_names();
        let mut cursor = QueryCursor::new();
        let mut matches = cursor.matches(&query, tree.root_node(), source.as_slice());
        while let Some(query_match) = matches.next() {
            for capture in query_match.captures {
                let node = capture.node;
                let position = node.start_position();
                let snippet = node
                    .utf8_text(&source)
                    .unwrap_or_default()
                    .lines()
                    .next()
                    .unwrap_or_default()
                    .trim()
                    .chars()
                    .take(500)
                    .collect();
                results.push(AstSearchResult {
                    path: file.relative_path.clone(),
                    line: position.row.saturating_add(1),
                    column: position.column.saturating_add(1),
                    capture: capture_names[capture.index as usize].to_owned(),
                    kind: node.kind().to_owned(),
                    snippet,
                });
                if results.len() >= request.limit {
                    return Ok(AstSearchResponse { results });
                }
            }
        }
    }
    Ok(AstSearchResponse { results })
}

#[cfg(test)]
mod tests {
    use super::{AstSearchRequest, search};
    use std::fs;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn search_returns_tree_sitter_query_captures() {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system time should be valid")
            .as_nanos();
        let root = std::env::temp_dir().join(format!(
            "shiori-ast-search-test-{}-{unique}",
            std::process::id()
        ));
        fs::create_dir_all(&root).expect("workspace should be created");
        fs::write(root.join("Sample.cs"), "public class Sample { }")
            .expect("source should be written");
        let request = AstSearchRequest {
            language: "csharp".to_owned(),
            pattern: "(class_declaration name: (identifier) @name)".to_owned(),
            path: None,
            limit: 20,
        };

        let response = search(&root, &request).expect("AST search should succeed");

        let result = response
            .results
            .first()
            .expect("capture should be returned");
        assert_eq!(result.path, "Sample.cs");
        assert_eq!(result.capture, "name");
        assert_eq!(result.snippet, "Sample");
        fs::remove_dir_all(root).expect("workspace should be removed");
    }
}
