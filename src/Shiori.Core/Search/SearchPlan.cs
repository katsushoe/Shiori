namespace Shiori.Core.Search;

/// <summary>The semantic intent inferred from a unified search query.</summary>
public enum SearchIntent
{
    File,
    Symbol,
    Text,
    References,
    Implementations,
}

/// <summary>A search provider selected by the query planner.</summary>
public enum SearchProvider
{
    File,
    Symbol,
    Text,
}

/// <summary>A deterministic provider plan for one user query.</summary>
public sealed record SearchPlan(
    string OriginalQuery,
    string SearchQuery,
    SearchIntent Intent,
    IReadOnlyList<SearchProvider> Providers,
    string Reason);
