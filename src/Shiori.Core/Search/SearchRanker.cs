namespace Shiori.Core.Search;

/// <summary>Scores, deduplicates, and orders unified search results.</summary>
public static class SearchRanker
{
    /// <summary>Returns the highest-value unique results for a query.</summary>
    public static IReadOnlyList<UnifiedSearchResult> Rank(
        string query,
        IEnumerable<UnifiedSearchResult> results,
        int limit,
        IReadOnlyDictionary<string, GitFileMetadata>? gitMetadata = null,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);

        return results
            .Select(result => result with { Score = Score(query, result, gitMetadata, now ?? DateTimeOffset.UtcNow) })
            .GroupBy(DeduplicationKey, StringComparer.OrdinalIgnoreCase)
            .Select(Merge)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => TypePriority(result.Type))
            .ThenBy(result => result.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Line)
            .Take(limit)
            .ToArray();
    }

    private static double Score(
        string query,
        UnifiedSearchResult result,
        IReadOnlyDictionary<string, GitFileMetadata>? gitMetadata,
        DateTimeOffset now)
    {
        var baseScore = BaseScore(query, result);
        if (gitMetadata is null || !gitMetadata.TryGetValue(NormalizePath(result.Path), out var metadata) || !metadata.IsTracked)
        {
            return baseScore;
        }

        var boost = 0.015;
        if (metadata.LastWriteTimeUtc is { } changed)
        {
            var age = now - changed;
            boost += age <= TimeSpan.FromDays(7) ? 0.025
                : age <= TimeSpan.FromDays(30) ? 0.0125
                : 0;
        }
        return Math.Min(1, baseScore + boost);
    }

    private static double BaseScore(string query, UnifiedSearchResult result)
    {
        if (result.Type == "symbol")
        {
            if (string.Equals(result.Name, query, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }
            if (result.Name?.StartsWith(query, StringComparison.OrdinalIgnoreCase) == true)
            {
                return 0.95;
            }
            if (string.Equals(result.QualifiedName, query, StringComparison.OrdinalIgnoreCase) ||
                result.QualifiedName?.EndsWith($"::{query}", StringComparison.OrdinalIgnoreCase) == true)
            {
                return 0.9;
            }
            return 0.6 + Math.Clamp(result.Score ?? 0, 0, 1) * 0.2;
        }

        if (result.Type == "file")
        {
            var fileName = Path.GetFileName(result.Path);
            var stem = Path.GetFileNameWithoutExtension(result.Path);
            if (string.Equals(fileName, query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stem, query, StringComparison.OrdinalIgnoreCase))
            {
                return 0.85;
            }
            return result.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ? 0.7 : 0.55;
        }

        return 0.5;
    }

    private static string DeduplicationKey(UnifiedSearchResult result) => result.Line is null
        ? $"{result.Type}:{NormalizePath(result.Path)}"
        : $"location:{NormalizePath(result.Path)}:{result.Line}";

    private static UnifiedSearchResult Merge(IGrouping<string, UnifiedSearchResult> group)
    {
        var ordered = group
            .OrderByDescending(result => result.Score)
            .ThenBy(result => TypePriority(result.Type))
            .ToArray();
        var best = ordered[0];
        var providers = ordered
            .SelectMany(result => result.MatchedProviders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return best with { MatchedProviders = providers };
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static int TypePriority(string type) => type switch
    {
        "symbol" => 0,
        "file" => 1,
        "text" => 2,
        _ => 3,
    };
}
