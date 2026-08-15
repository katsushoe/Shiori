use std::path::Path;
use tree_sitter::{Language, Parser, Tree};

pub const SUPPORTED_LANGUAGES: &[&str] = &[
    "c",
    "cpp",
    "csharp",
    "go",
    "java",
    "javascript",
    "python",
    "rust",
    "typescript",
];
pub const PARSER_VERSION: &str = "tree-sitter-0.26.12";

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum LanguageId {
    C,
    Cpp,
    CSharp,
    Go,
    Java,
    JavaScript,
    Python,
    Rust,
    TypeScript,
    Tsx,
}

impl LanguageId {
    pub fn name(self) -> &'static str {
        match self {
            Self::C => "c",
            Self::Cpp => "cpp",
            Self::CSharp => "csharp",
            Self::Go => "go",
            Self::Java => "java",
            Self::JavaScript => "javascript",
            Self::Python => "python",
            Self::Rust => "rust",
            Self::TypeScript | Self::Tsx => "typescript",
        }
    }

    fn grammar(self) -> Language {
        match self {
            Self::C => tree_sitter_c::LANGUAGE.into(),
            Self::Cpp => tree_sitter_cpp::LANGUAGE.into(),
            Self::CSharp => tree_sitter_c_sharp::LANGUAGE.into(),
            Self::Go => tree_sitter_go::LANGUAGE.into(),
            Self::Java => tree_sitter_java::LANGUAGE.into(),
            Self::JavaScript => tree_sitter_javascript::LANGUAGE.into(),
            Self::Python => tree_sitter_python::LANGUAGE.into(),
            Self::Rust => tree_sitter_rust::LANGUAGE.into(),
            Self::TypeScript => tree_sitter_typescript::LANGUAGE_TYPESCRIPT.into(),
            Self::Tsx => tree_sitter_typescript::LANGUAGE_TSX.into(),
        }
    }
}

pub fn detect(path: &Path) -> Option<LanguageId> {
    let extension = path.extension()?.to_str()?.to_ascii_lowercase();
    match extension.as_str() {
        "c" | "h" => Some(LanguageId::C),
        "cc" | "cpp" | "cxx" | "hh" | "hpp" | "hxx" => Some(LanguageId::Cpp),
        "cs" => Some(LanguageId::CSharp),
        "go" => Some(LanguageId::Go),
        "java" => Some(LanguageId::Java),
        "js" | "jsx" | "mjs" | "cjs" => Some(LanguageId::JavaScript),
        "py" => Some(LanguageId::Python),
        "rs" => Some(LanguageId::Rust),
        "ts" | "mts" | "cts" => Some(LanguageId::TypeScript),
        "tsx" => Some(LanguageId::Tsx),
        _ => None,
    }
}

pub fn parse(language: LanguageId, source: &[u8]) -> Result<Tree, String> {
    let mut parser = Parser::new();
    parser
        .set_language(&language.grammar())
        .map_err(|source| format!("cannot load {} grammar: {source}", language.name()))?;
    parser
        .parse(source, None)
        .ok_or_else(|| format!("{} parsing was cancelled", language.name()))
}

pub fn validate_parsers() -> Result<(), String> {
    let languages = [
        LanguageId::C,
        LanguageId::Cpp,
        LanguageId::CSharp,
        LanguageId::Go,
        LanguageId::Java,
        LanguageId::JavaScript,
        LanguageId::Python,
        LanguageId::Rust,
        LanguageId::TypeScript,
        LanguageId::Tsx,
    ];
    for language in languages {
        parse(language, b"")?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{LanguageId, detect, parse};
    use std::path::Path;

    #[test]
    fn detect_recognizes_v1_language_extensions() {
        let cases = [
            ("sample.c", LanguageId::C),
            ("sample.hpp", LanguageId::Cpp),
            ("sample.cs", LanguageId::CSharp),
            ("sample.go", LanguageId::Go),
            ("sample.java", LanguageId::Java),
            ("sample.js", LanguageId::JavaScript),
            ("sample.py", LanguageId::Python),
            ("sample.rs", LanguageId::Rust),
            ("sample.ts", LanguageId::TypeScript),
            ("sample.tsx", LanguageId::Tsx),
        ];

        for (path, expected) in cases {
            assert_eq!(detect(Path::new(path)), Some(expected));
        }
        assert_eq!(detect(Path::new("README.md")), None);
    }

    #[test]
    fn parse_builds_syntax_trees_for_v1_languages() {
        let cases = [
            (LanguageId::C, "int main(void) { return 0; }"),
            (LanguageId::Cpp, "class Sample { public: void Run() {} };"),
            (
                LanguageId::CSharp,
                "namespace Demo; public class Sample { }",
            ),
            (LanguageId::Go, "package main\nfunc main() {}"),
            (LanguageId::Java, "class Sample { void run() {} }"),
            (LanguageId::JavaScript, "export function sample() {}"),
            (LanguageId::Python, "def sample():\n    return 1\n"),
            (LanguageId::Rust, "fn sample() {}"),
            (LanguageId::TypeScript, "export function sample(): void {}"),
            (LanguageId::Tsx, "export const Sample = () => <div />;"),
        ];

        for (language, source) in cases {
            let tree = parse(language, source.as_bytes()).expect("source should parse");
            assert!(
                !tree.root_node().has_error(),
                "{} parse failed",
                language.name()
            );
        }
    }
}
