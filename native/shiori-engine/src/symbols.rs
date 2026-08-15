use crate::languages::LanguageId;
use tree_sitter::{Node, Tree};

pub struct ExtractedSymbol {
    pub name: String,
    pub qualified_name: String,
    pub kind: &'static str,
    pub language: &'static str,
    pub start_line: i64,
    pub start_column: i64,
    pub end_line: i64,
    pub end_column: i64,
    pub parent_index: Option<usize>,
    pub signature: String,
}

pub fn extract(language: LanguageId, source: &[u8], tree: &Tree) -> Vec<ExtractedSymbol> {
    let mut symbols = Vec::new();
    if language == LanguageId::CSharp {
        let root = tree.root_node();
        let mut cursor = root.walk();
        let children = root.named_children(&mut cursor).collect::<Vec<_>>();
        if let Some(namespace) = children
            .iter()
            .copied()
            .find(|node| node.kind() == "file_scoped_namespace_declaration")
        {
            visit(namespace, source, language, None, None, &mut symbols);
            if let Some(parent) = symbols.first() {
                let parent_name = parent.qualified_name.clone();
                for child in children.into_iter().filter(|node| *node != namespace) {
                    visit(
                        child,
                        source,
                        language,
                        Some(0),
                        Some(&parent_name),
                        &mut symbols,
                    );
                }
            }
            return symbols;
        }
    }
    visit(tree.root_node(), source, language, None, None, &mut symbols);
    symbols
}

fn visit(
    node: Node<'_>,
    source: &[u8],
    language: LanguageId,
    parent_index: Option<usize>,
    parent_qualified_name: Option<&str>,
    symbols: &mut Vec<ExtractedSymbol>,
) {
    let mut child_parent_index = parent_index;
    let mut child_qualified_name = parent_qualified_name.map(str::to_owned);
    if let Some(mut kind) = symbol_kind(node.kind())
        && let Some(name) = symbol_name(node, source)
    {
        if kind == "function" && is_method_context(node) {
            kind = "method";
        }
        let qualified_name = match parent_qualified_name {
            Some(parent) => format!("{parent}::{name}"),
            None => name.clone(),
        };
        let start = node.start_position();
        let end = node.end_position();
        let symbol_index = symbols.len();
        symbols.push(ExtractedSymbol {
            name,
            qualified_name: qualified_name.clone(),
            kind,
            language: language.name(),
            start_line: position_value(start.row),
            start_column: position_value(start.column),
            end_line: position_value(end.row),
            end_column: position_value(end.column),
            parent_index,
            signature: signature(node, source),
        });
        child_parent_index = Some(symbol_index);
        child_qualified_name = Some(qualified_name);
    }

    let mut cursor = node.walk();
    for child in node.named_children(&mut cursor) {
        visit(
            child,
            source,
            language,
            child_parent_index,
            child_qualified_name.as_deref(),
            symbols,
        );
    }
}

fn symbol_kind(node_kind: &str) -> Option<&'static str> {
    match node_kind {
        "namespace_declaration" | "file_scoped_namespace_declaration" | "namespace_definition" => {
            Some("namespace")
        }
        "class_declaration" | "class_definition" | "class_specifier" => Some("class"),
        "interface_declaration" => Some("interface"),
        "record_declaration" => Some("record"),
        "struct_declaration" | "struct_item" | "struct_specifier" => Some("struct"),
        "enum_declaration" | "enum_item" | "enum_specifier" => Some("enum"),
        "trait_item" => Some("trait"),
        "type_alias_declaration" | "type_item" | "type_definition" | "type_spec" => Some("type"),
        "module" | "mod_item" => Some("module"),
        "function_declaration" | "function_definition" | "function_item" => Some("function"),
        "method_declaration" | "method_definition" => Some("method"),
        "constructor_declaration" => Some("constructor"),
        "property_declaration" => Some("property"),
        "field_declaration" => Some("field"),
        "const_item" | "constant_declaration" => Some("constant"),
        _ => None,
    }
}

fn symbol_name(node: Node<'_>, source: &[u8]) -> Option<String> {
    if let Some(name) = node.child_by_field_name("name") {
        return node_text(name, source);
    }
    if matches!(node.kind(), "field_declaration" | "constant_declaration")
        && let Some(declarator) =
            find_descendant(node, &["variable_declarator", "variable_declaration"])
    {
        return declarator
            .child_by_field_name("name")
            .and_then(|name| node_text(name, source))
            .or_else(|| find_identifier(declarator, source));
    }
    node.child_by_field_name("declarator")
        .and_then(|declarator| find_identifier(declarator, source))
        .or_else(|| find_identifier(node, source))
}

fn find_descendant<'tree>(node: Node<'tree>, kinds: &[&str]) -> Option<Node<'tree>> {
    if kinds.contains(&node.kind()) {
        return Some(node);
    }
    let mut cursor = node.walk();
    for child in node.named_children(&mut cursor) {
        if let Some(found) = find_descendant(child, kinds) {
            return Some(found);
        }
    }
    None
}

fn find_identifier(node: Node<'_>, source: &[u8]) -> Option<String> {
    if matches!(
        node.kind(),
        "identifier" | "type_identifier" | "field_identifier" | "namespace_identifier"
    ) {
        return node_text(node, source);
    }
    if let Some(name) = node.child_by_field_name("name") {
        return node_text(name, source);
    }
    if let Some(declarator) = node.child_by_field_name("declarator")
        && let Some(name) = find_identifier(declarator, source)
    {
        return Some(name);
    }
    let mut cursor = node.walk();
    for child in node.named_children(&mut cursor) {
        if let Some(name) = find_identifier(child, source) {
            return Some(name);
        }
    }
    None
}

fn is_method_context(node: Node<'_>) -> bool {
    let mut ancestor = node.parent();
    while let Some(parent) = ancestor {
        if matches!(
            parent.kind(),
            "class_body"
                | "class_definition"
                | "class_declaration"
                | "class_specifier"
                | "impl_item"
                | "interface_body"
                | "struct_declaration"
                | "struct_item"
        ) {
            return true;
        }
        ancestor = parent.parent();
    }
    false
}

fn signature(node: Node<'_>, source: &[u8]) -> String {
    node.utf8_text(source)
        .unwrap_or_default()
        .lines()
        .next()
        .unwrap_or_default()
        .trim()
        .chars()
        .take(500)
        .collect()
}

fn node_text(node: Node<'_>, source: &[u8]) -> Option<String> {
    node.utf8_text(source)
        .ok()
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .map(str::to_owned)
}

fn position_value(value: usize) -> i64 {
    i64::try_from(value.saturating_add(1)).unwrap_or(i64::MAX)
}

#[cfg(test)]
mod tests {
    use super::extract;
    use crate::languages::{LanguageId, parse};

    #[test]
    fn extract_finds_csharp_hierarchy_and_members() {
        let source = b"namespace Demo { public class Sample { private int count; public string Name { get; } public void Run() {} } }";
        let tree = parse(LanguageId::CSharp, source).expect("C# should parse");

        let symbols = extract(LanguageId::CSharp, source, &tree);

        assert_symbol(&symbols, "Demo", "namespace");
        assert_symbol(&symbols, "Sample", "class");
        assert_symbol(&symbols, "count", "field");
        assert_symbol(&symbols, "Name", "property");
        assert_symbol(&symbols, "Run", "method");
        assert!(
            symbols
                .iter()
                .any(|symbol| symbol.qualified_name == "Demo::Sample::Run")
        );
    }

    #[test]
    fn extract_applies_file_scoped_csharp_namespace() {
        let source = b"namespace Demo; public record Sample();";
        let tree = parse(LanguageId::CSharp, source).expect("C# should parse");

        let symbols = extract(LanguageId::CSharp, source, &tree);

        assert!(symbols.iter().any(|symbol| {
            symbol.qualified_name == "Demo::Sample" && symbol.parent_index == Some(0)
        }));
    }

    #[test]
    fn extract_finds_symbols_for_all_v1_languages() {
        let cases = [
            (LanguageId::C, "int sample(void) { return 0; }", "sample"),
            (LanguageId::Cpp, "class Sample { void run() {} };", "Sample"),
            (LanguageId::Go, "package main\nfunc sample() {}", "sample"),
            (LanguageId::Java, "class Sample { void run() {} }", "Sample"),
            (LanguageId::JavaScript, "function sample() {}", "sample"),
            (
                LanguageId::Python,
                "def sample():\n    return 1\n",
                "sample",
            ),
            (LanguageId::Rust, "fn sample() {}", "sample"),
            (LanguageId::TypeScript, "interface Sample {}", "Sample"),
            (
                LanguageId::Tsx,
                "function Sample() { return <div />; }",
                "Sample",
            ),
        ];

        for (language, source, expected) in cases {
            let tree = parse(language, source.as_bytes()).expect("source should parse");
            let symbols = extract(language, source.as_bytes(), &tree);
            assert!(
                symbols.iter().any(|symbol| symbol.name == expected),
                "{} did not extract {expected}",
                language.name()
            );
        }
    }

    fn assert_symbol(symbols: &[super::ExtractedSymbol], name: &str, kind: &str) {
        assert!(
            symbols
                .iter()
                .any(|symbol| symbol.name == name && symbol.kind == kind),
            "missing {kind} {name}"
        );
    }
}
