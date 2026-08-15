using System.Text.Json.Serialization;

namespace Shiori.Core.Search;

/// <summary>The semantic intent inferred from a unified search query.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SearchIntent>))]
public enum SearchIntent
{
    [JsonStringEnumMemberName("file")]
    File,
    [JsonStringEnumMemberName("symbol")]
    Symbol,
    [JsonStringEnumMemberName("text")]
    Text,
    [JsonStringEnumMemberName("references")]
    References,
    [JsonStringEnumMemberName("implementations")]
    Implementations,
}

/// <summary>A search provider selected by the query planner.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SearchProvider>))]
public enum SearchProvider
{
    [JsonStringEnumMemberName("file")]
    File,
    [JsonStringEnumMemberName("symbol")]
    Symbol,
    [JsonStringEnumMemberName("text")]
    Text,
}

/// <summary>A deterministic provider plan for one user query.</summary>
public sealed record SearchPlan(
    string OriginalQuery,
    string SearchQuery,
    SearchIntent Intent,
    IReadOnlyList<SearchProvider> Providers,
    string Reason);
