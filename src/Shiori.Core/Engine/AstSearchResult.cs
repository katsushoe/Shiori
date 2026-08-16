namespace Shiori.Core.Engine;

/// <summary>Represents one Tree-sitter query capture.</summary>
public sealed record AstSearchResult(
    string Path,
    int Line,
    int Column,
    string Capture,
    string Kind,
    string Snippet);
