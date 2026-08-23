using Shiori.Cli.Server;
using Shiori.Core.Engine;
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
        Assert.Contains(response.Results, result => result.WorkspaceId == "first" && result.Path == "FirstResult.cs");
        Assert.Contains(response.Results, result => result.WorkspaceId == "second" && result.Path == "SecondResult.cs");
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
    }

    private static FakeEngine CreateEngine(
        string id,
        string name,
        string resultPath,
        bool failSearch = false)
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"shiori-{id}"));
        return new FakeEngine(
            new WorkspaceInfo(id, path, name, Path.Combine(path, "shiori.db"), 1),
            resultPath,
            failSearch);
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
        bool failSearch) : IShioriEngine
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
            Info.Id, "ready", 1, 1, 1, null, null);
    }
}
