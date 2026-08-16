namespace Shiori.Core.Search;

/// <summary>A provider-neutral search result for agents and CLI clients.</summary>
public sealed record UnifiedSearchResult(
    string Type,
    string Provider,
    string Path,
    long? Line,
    long? Column,
    string? Name,
    string? Kind,
    string? Language,
    double? Score,
    string? Snippet,
    string? QualifiedName,
    IReadOnlyList<string> MatchedProviders);

/// <summary>A unified search response including its execution plan and recoverable provider errors.</summary>
public sealed record UnifiedSearchResponse(
    SearchPlan Plan,
    IReadOnlyList<UnifiedSearchResult> Results,
    IReadOnlyDictionary<string, string> ProviderErrors);
