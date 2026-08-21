using Shiori.Core.Engine;
using Shiori.Core.Search;
using System.Text.Json;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class UnifiedSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_falls_back_when_git_metadata_is_unavailable()
    {
        using var engine = new FakeEngine();

        var response = await UnifiedSearchService.SearchAsync(
            engine,
            "SaveAccount",
            gitMetadataProvider: new ThrowingGitMetadataProvider());

        Assert.NotEmpty(response.Results);
    }

    [Fact]
    public async Task SearchAsync_executes_planned_providers_and_bounds_combined_results()
    {
        using var engine = new FakeEngine();

        var response = await UnifiedSearchService.SearchAsync(engine, "SaveAccount", limit: 2);

        Assert.Equal(1, engine.FileSearches);
        Assert.Equal(1, engine.SymbolSearches);
        Assert.Equal(1, engine.TextSearches);
        Assert.Equal(2, response.Results.Count);
        Assert.Equal(["symbol", "file"], response.Results.Select(result => result.Provider));
        Assert.Empty(response.ProviderErrors);
    }

    [Fact]
    public async Task SearchAsync_runs_only_text_for_a_quoted_phrase()
    {
        using var engine = new FakeEngine();

        var response = await UnifiedSearchService.SearchAsync(engine, "\"exact failure message\"");

        Assert.Equal("exact failure message", response.Plan.SearchQuery);
        Assert.Equal(0, engine.FileSearches);
        Assert.Equal(0, engine.SymbolSearches);
        Assert.Equal(1, engine.TextSearches);
    }

    [Fact]
    public async Task SearchAsync_reports_one_provider_failure_without_losing_other_results()
    {
        using var engine = new FakeEngine { FailTextSearch = true };

        var response = await UnifiedSearchService.SearchAsync(engine, "SaveAccount");

        Assert.Contains("text", response.ProviderErrors.Keys);
        Assert.Contains(response.Results, result => result.Provider == "symbol");
        Assert.Contains(response.Results, result => result.Provider == "file");
    }

    [Fact]
    public void SearchPlan_serializes_intent_and_providers_as_names()
    {
        var json = JsonSerializer.Serialize(QueryPlanner.Plan("SaveAccount"), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        Assert.Contains("\"intent\":\"symbol\"", json);
        Assert.Contains("\"providers\":[\"symbol\",\"file\",\"text\"]", json);
    }

    private sealed class ThrowingGitMetadataProvider : IGitMetadataProvider
    {
        public IReadOnlyDictionary<string, GitFileMetadata> GetMetadata(
            string workspaceRoot,
            IEnumerable<string> relativePaths) => throw new IOException("git unavailable");
    }

    private sealed class FakeEngine : IShioriEngine
    {
        private int _fileSearches;
        private int _symbolSearches;
        private int _textSearches;

        public uint AbiVersion => 2;
        public int FileSearches => _fileSearches;
        public int SymbolSearches => _symbolSearches;
        public int TextSearches => _textSearches;
        public bool FailTextSearch { get; init; }

        public IReadOnlyList<SearchResult> SearchFiles(string query, int limit = 20)
        {
            Interlocked.Increment(ref _fileSearches);
            return [new SearchResult("file", "SaveAccount.cs", null, null)];
        }

        public IReadOnlyList<SymbolSearchResult> SearchSymbols(
            string query,
            string? kind = null,
            string? language = null,
            string? path = null,
            int limit = 20)
        {
            Interlocked.Increment(ref _symbolSearches);
            return [new SymbolSearchResult(
                "symbol", "SaveAccount", "Service::SaveAccount", "method", "csharp",
                "Service.cs", 10, 5, 1, "void SaveAccount()")];
        }

        public IReadOnlyList<AstSearchResult> SearchAst(
            string language,
            string pattern,
            string? path = null,
            int limit = 20) => [];

        public IReadOnlyList<SearchResult> SearchText(
            string query,
            string? path = null,
            string? glob = null,
            bool regex = false,
            bool caseSensitive = false,
            int contextLines = 0,
            int limit = 20)
        {
            Interlocked.Increment(ref _textSearches);
            if (FailTextSearch)
            {
                throw new InvalidOperationException("ripgrep unavailable");
            }
            return [new SearchResult("text", "Service.cs", 10, "SaveAccount();", 5)];
        }

        public WorkspaceInfo GetWorkspaceInfo() => new(
            "test",
            AppContext.BaseDirectory,
            "test",
            Path.Combine(AppContext.BaseDirectory, "test.db"),
            1);
        public IndexStatus GetIndexStatus() => throw new NotSupportedException();
        public IndexStatus BuildIndex() => throw new NotSupportedException();
        public IndexStatus RebuildIndex() => throw new NotSupportedException();
        public FileOutline GetFileOutline(string path) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
