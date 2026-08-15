namespace Shiori.Core.Engine;

/// <summary>Contains bounded file-search results.</summary>
public sealed record SearchFilesResponse(IReadOnlyList<SearchResult> Results);
