using Shiori.Cli.Server;
using Shiori.Core.Engine;
using System.Text.Json;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class WorkspaceCoordinatorTests
{
    [Fact]
    public async Task SearchFilesAsync_MultipleWorkspaces_TagsAndMergesResults()
    {
        var first = CreateEngine("first", "First", "FirstResult.cs");
        var second = CreateEngine("second", "Second", "SecondResult.cs");
        var coordinator = new WorkspaceCoordinator(new FakeProvider(first, second));

        var response = await coordinator.SearchFilesAsync("Result", null, 10, CancellationToken.None);

        Assert.Empty(response.Errors);
        Assert.Equal(2, response.Results.Count);
        Assert.Equal(2, response.Workspaces.Count);
        Assert.True(response.ElapsedMilliseconds >= 0);
        Assert.All(response.Workspaces, summary => Assert.Equal("OK", summary.SearchResult));
        Assert.Contains("| First | 1 | 1 | OK | 1 | ready |", response.SummaryMarkdown, StringComparison.Ordinal);
        Assert.Contains(response.Results, result => result.WorkspaceId == "first" && result.Path == "FirstResult.cs");
        Assert.Contains(response.Results, result => result.WorkspaceId == "second" && result.Path == "SecondResult.cs");
    }

    [Fact]
    public async Task SearchFilesAsync_WhenSerialized_ContainsElapsedMilliseconds()
    {
        var engine = CreateEngine("first", "First", "FirstResult.cs");
        var coordinator = new WorkspaceCoordinator(new FakeProvider(engine));

        var response = await coordinator.SearchFilesAsync("Result", null, 10, CancellationToken.None);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"elapsedMilliseconds\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchFilesAsync_WorkspaceFailure_ReturnsOtherResultsAndStructuredError()
    {
        var healthy = CreateEngine("healthy", "Healthy", "Result.cs");
        var failed = CreateEngine("failed", "Failed", "Ignored.cs", failSearch: true);
        var coordinator = new WorkspaceCoordinator(new FakeProvider(healthy, failed));

        var response = await coordinator.SearchFilesAsync("Result", null, 10, CancellationToken.None);

        Assert.Single(response.Results);
        var error = Assert.Single(response.Errors);
        Assert.Equal(failed.Info.Path, error.Workspace);
        Assert.Equal("search failed", error.Message);
        var summary = Assert.Single(response.Workspaces, item => item.WorkspaceId == "failed");
        Assert.Equal("NG", summary.SearchResult);
        Assert.Equal(0, summary.HitCount);
        Assert.Null(summary.ActionRequired);
    }

    [Fact]
    public async Task SearchFilesAsync_IndexNotCreated_ReturnsSummaryAndConfirmationAction()
    {
        var engine = CreateEngine("empty", "Empty", "Ignored.cs", status: "not_indexed");
        var coordinator = new WorkspaceCoordinator(new FakeProvider(engine));

        var response = await coordinator.SearchFilesAsync("Result", null, 10, CancellationToken.None);

        Assert.Empty(response.Results);
        Assert.Single(response.Errors);
        var summary = Assert.Single(response.Workspaces);
        Assert.Equal("Empty", summary.WorkspaceName);
        Assert.Equal(0, summary.SearchTargetDirectories);
        Assert.Equal(0, summary.SearchTargetFiles);
        Assert.Equal("NG", summary.SearchResult);
        Assert.Equal(0, summary.HitCount);
        Assert.Equal("not_indexed", summary.IndexStatus);
        Assert.Equal("index_build_confirmation", summary.ActionRequired);
        Assert.Equal("index_build", summary.SuggestedTool);
    }

    [Fact]
    public async Task SearchFilesAsync_InterruptedIndexWithoutPublishedGeneration_ReturnsResumeConfirmation()
    {
        var engine = CreateEngine("interrupted", "Interrupted", "Ignored.cs", failSearch: true, status: "indexing");
        var coordinator = new WorkspaceCoordinator(new FakeProvider(engine));

        var response = await coordinator.SearchFilesAsync("Result", null, 10, CancellationToken.None);

        var summary = Assert.Single(response.Workspaces);
        Assert.Equal("NG", summary.SearchResult);
        Assert.Equal("indexing", summary.IndexStatus);
        Assert.Equal("index_resume_confirmation", summary.ActionRequired);
        Assert.Equal("index_build", summary.SuggestedTool);
    }

    private static FakeEngine CreateEngine(
        string id,
        string name,
        string resultPath,
        bool failSearch = false,
        string status = "ready")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"shiori-{id}"));
        return new FakeEngine(
            new WorkspaceInfo(id, path, name, Path.Combine(path, "shiori.db"), 1),
            resultPath,
            failSearch,
            status);
    }

    private sealed class FakeProvider(params FakeEngine[] engines) : IWorkspaceEngineProvider
    {
        private readonly IReadOnlyDictionary<string, FakeEngine> _engines =
            engines.ToDictionary(engine => engine.Info.Path, StringComparer.OrdinalIgnoreCase);

        public IShioriEngine GetEngine(string workspace) => _engines[workspace];

        public IReadOnlyList<string> ResolveWorkspacePaths(IReadOnlyList<string>? requested)
        {
            var paths = requested is null || requested.Count == 0 ? _engines.Keys : requested;
            var result = paths.Select(Path.GetFullPath).ToArray();
            if (result.Any(path => !_engines.ContainsKey(path)))
            {
                throw new UnauthorizedAccessException();
            }

            return result;
        }
    }

    private sealed class FakeEngine(
        WorkspaceInfo info,
        string resultPath,
        bool failSearch,
        string status) : IShioriEngine
    {
        public uint AbiVersion => 3;
        public WorkspaceInfo Info { get; } = info;

        public WorkspaceInfo GetWorkspaceInfo() => Info;
        public IndexStatus GetIndexStatus() => Status();
        public ulong CountIndexDirectories() => 1;
        public IndexStatus BuildIndex(ulong totalDirectories, Action<IndexProgress>? progress = null) => Status();

        public IReadOnlyList<SearchResult> SearchFiles(string query, int limit = 20)
        {
            if (failSearch) throw new InvalidOperationException("search failed");
            return [new SearchResult("file", resultPath, null, null)];
        }

        public void Dispose() { }

        private IndexStatus Status() => new(
            Info.Id,
            status,
            string.Equals(status, "not_indexed", StringComparison.Ordinal) ? 0 : 1,
            string.Equals(status, "not_indexed", StringComparison.Ordinal) ? 0 : 1,
            1,
            null,
            null);
    }
}
