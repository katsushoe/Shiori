namespace Shiori.Core.Engine;

/// <summary>An indexed source symbol matching a symbol search.</summary>
public sealed record SymbolSearchResult(
    string Type,
    string Name,
    string? QualifiedName,
    string Kind,
    string Language,
    string Path,
    long Line,
    long Column,
    double Score,
    string? Signature);

/// <summary>A bounded set of indexed symbol matches.</summary>
public sealed record SearchSymbolsResponse(IReadOnlyList<SymbolSearchResult> Results);
