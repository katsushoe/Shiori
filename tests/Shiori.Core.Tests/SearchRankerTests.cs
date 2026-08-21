using Shiori.Core.Search;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class SearchRankerTests
{
    [Fact]
    public void Rank_orders_exact_prefix_filename_path_and_text_matches()
    {
        var results = new[]
        {
            Result("text", "text", "src/Other.cs", 30, snippet: "SaveAccount();"),
            Result("file", "file", "src/SaveAccount.cs", null),
            Result("symbol", "symbol", "src/Prefix.cs", 20, name: "SaveAccountAsync", score: 0.9),
            Result("symbol", "symbol", "src/Exact.cs", 10, name: "SaveAccount", score: 1),
            Result("file", "file", "src/services/SaveAccountHelper.cs", null),
        };

        var ranked = SearchRanker.Rank("SaveAccount", results, 10);

        Assert.Equal("src/Exact.cs", ranked[0].Path);
        Assert.Equal("src/Prefix.cs", ranked[1].Path);
        Assert.Equal("src/SaveAccount.cs", ranked[2].Path);
        Assert.Equal("src/services/SaveAccountHelper.cs", ranked[3].Path);
        Assert.Equal("src/Other.cs", ranked[4].Path);
        Assert.Equal([1.0, 0.95, 0.85, 0.7, 0.5], ranked.Select(result => result.Score));
    }

    [Fact]
    public void Rank_deduplicates_a_code_location_and_preserves_matching_providers()
    {
        var results = new[]
        {
            Result("symbol", "symbol", "Service.cs", 42, name: "SaveAccount", score: 1),
            Result("text", "text", "Service.cs", 42, snippet: "void SaveAccount()"),
        };

        var ranked = SearchRanker.Rank("SaveAccount", results, 10);

        var result = Assert.Single(ranked);
        Assert.Equal("symbol", result.Type);
        Assert.Equal(["symbol", "text"], result.MatchedProviders);
    }

    [Fact]
    public void Rank_applies_the_combined_limit_after_deduplication()
    {
        var results = Enumerable.Range(1, 5)
            .Select(line => Result("text", "text", "Service.cs", line))
            .ToArray();

        Assert.Equal(3, SearchRanker.Rank("value", results, 3).Count);
    }

    [Fact]
    public void Rank_applies_bounded_tracked_and_recent_change_boosts()
    {
        var now = new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
        var results = new[]
        {
            Result("text", "text", "src/old.cs", 1),
            Result("text", "text", "src/recent.cs", 1),
            Result("text", "text", "src/untracked.cs", 1),
        };
        var metadata = new Dictionary<string, GitFileMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/old.cs"] = new(true, now.AddDays(-60)),
            ["src/recent.cs"] = new(true, now.AddDays(-2)),
            ["src/untracked.cs"] = new(false, now),
        };

        var ranked = SearchRanker.Rank("value", results, 10, metadata, now);

        Assert.Equal(["src/recent.cs", "src/old.cs", "src/untracked.cs"], ranked.Select(result => result.Path));
        Assert.Equal([0.54, 0.515, 0.5], ranked.Select(result => result.Score));
    }

    [Fact]
    public void Rank_keeps_match_quality_ahead_of_git_boosts()
    {
        var now = DateTimeOffset.UtcNow;
        var results = new[]
        {
            Result("file", "file", "src/SaveAccount.cs", null),
            Result("file", "file", "src/SaveAccountHelper.cs", null),
        };
        var metadata = new Dictionary<string, GitFileMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/SaveAccountHelper.cs"] = new(true, now),
        };

        var ranked = SearchRanker.Rank("SaveAccount", results, 10, metadata, now);

        Assert.Equal("src/SaveAccount.cs", ranked[0].Path);
    }

    private static UnifiedSearchResult Result(
        string type,
        string provider,
        string path,
        long? line,
        string? name = null,
        double? score = null,
        string? snippet = null) => new(
            type, provider, path, line, 1, name, null, null, score, snippet, null, [provider]);
}
