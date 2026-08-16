namespace Shiori.Core.Engine;

/// <summary>Contains bounded Tree-sitter query captures.</summary>
public sealed record AstSearchResponse(IReadOnlyList<AstSearchResult> Results);
