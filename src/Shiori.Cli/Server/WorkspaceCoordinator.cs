using Shiori.Core.Engine;

namespace Shiori.Cli.Server;

/// <summary>Fans work out to isolated workspace engines and merges deterministic results.</summary>
public sealed class WorkspaceCoordinator
{
    private const int MaximumConcurrency = 8;
    private readonly IWorkspaceEngineProvider _engines;

    /// <summary>Creates a coordinator over the configured workspace provider.</summary>
    public WorkspaceCoordinator(IWorkspaceEngineProvider engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        _engines = engines;
    }

    /// <summary>Searches selected workspaces concurrently and returns workspace-tagged results.</summary>
    public async Task<WorkspaceSearchFilesResponse> SearchFilesAsync(
        string query,
        IReadOnlyList<string>? workspaces,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be from 1 to 100.");
        }

        var paths = _engines.ResolveWorkspacePaths(workspaces);
        using var concurrency = CreateConcurrencyGate(paths.Count);
        var searches = paths.Select(path => SearchWorkspaceAsync(
            path, query, limit, concurrency, cancellationToken));
        var responses = await Task.WhenAll(searches).ConfigureAwait(false);
        var results = responses
            .SelectMany(response => response.Results)
            .OrderBy(result => FileRank(result.Path, query))
            .ThenBy(result => result.Path.Length)
            .ThenBy(result => result.WorkspaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Path, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
        var errors = responses
            .Where(response => response.Error is not null)
            .Select(response => response.Error!)
            .ToArray();
        var summaries = responses.Select(response => new WorkspaceSearchSummary(
            response.Info.Id,
            response.Info.Name,
            response.Info.Path,
            response.Status.IndexedDirectories ?? 0,
            response.Status.IndexedFiles,
            response.Error is null ? "OK" : "NG",
            results.Count(result => string.Equals(
                result.WorkspaceId,
                response.Info.Id,
                StringComparison.Ordinal)),
            response.Status.Status,
            GetActionRequired(response.Status, response.Error),
            GetSuggestedTool(response.Status, response.Error),
            response.Error?.Message)).ToArray();
        return new WorkspaceSearchFilesResponse(results, errors, summaries, FormatSummaryTable(summaries));
    }

    private async Task<SearchWorkspaceResponse> SearchWorkspaceAsync(
        string workspace,
        string query,
        int limit,
        SemaphoreSlim concurrency,
        CancellationToken cancellationToken)
    {
        await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var engine = _engines.GetEngine(workspace);
                var info = engine.GetWorkspaceInfo();
                var status = engine.GetIndexStatus();
                if (string.Equals(status.Status, "not_indexed", StringComparison.Ordinal))
                {
                    return new SearchWorkspaceResponse(
                        [],
                        new WorkspaceOperationError(workspace, "Workspace index has not been created."),
                        info,
                        status);
                }
                var results = engine.SearchFiles(query, limit).Select(result => new WorkspaceSearchResult(
                    info.Id, info.Name, info.Path, result.Type, result.Path,
                    result.Line, result.Snippet, result.Column)).ToArray();
                return new SearchWorkspaceResponse(results, null, info, status);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var engine = _engines.GetEngine(workspace);
            return new SearchWorkspaceResponse(
                [],
                new WorkspaceOperationError(workspace, exception.Message),
                engine.GetWorkspaceInfo(),
                engine.GetIndexStatus());
        }
        finally
        {
            concurrency.Release();
        }
    }

    private static SemaphoreSlim CreateConcurrencyGate(int workspaceCount) =>
        new(Math.Max(1, Math.Min(workspaceCount, Math.Min(Environment.ProcessorCount, MaximumConcurrency))));

    private static string? GetActionRequired(IndexStatus status, WorkspaceOperationError? error)
    {
        if (error is null) return null;
        return string.Equals(status.Status, "not_indexed", StringComparison.Ordinal)
            ? "index_build_confirmation"
            : string.Equals(status.Status, "indexing", StringComparison.Ordinal)
                ? "index_resume_confirmation"
                : null;
    }

    private static string? GetSuggestedTool(IndexStatus status, WorkspaceOperationError? error) =>
        GetActionRequired(status, error) is null ? null : "index_build";

    private static string FormatSummaryTable(IReadOnlyList<WorkspaceSearchSummary> summaries)
    {
        var lines = new List<string>
        {
            "| 検索対象ワークスペース名 | 検索対象ディレクトリ数 | 検索対象ファイル数 | 検索結果 | 検索結果ヒット数 | インデックスステータス |",
            "|---|---:|---:|:---:|---:|---|"
        };
        lines.AddRange(summaries.Select(summary =>
            $"| {EscapeTableCell(summary.WorkspaceName)} | {summary.SearchTargetDirectories} | {summary.SearchTargetFiles} | {summary.SearchResult} | {summary.HitCount} | {EscapeTableCell(summary.IndexStatus)} |"));
        return string.Join('\n', lines);
    }

    private static string EscapeTableCell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static int FileRank(string path, string query)
    {
        var name = Path.GetFileName(path);
        if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private sealed record SearchWorkspaceResponse(
        IReadOnlyList<WorkspaceSearchResult> Results,
        WorkspaceOperationError? Error,
        WorkspaceInfo Info,
        IndexStatus Status);
}
