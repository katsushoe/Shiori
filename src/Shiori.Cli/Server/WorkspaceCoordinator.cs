using System.Collections.Concurrent;
using System.Diagnostics;
using Shiori.Core.Engine;

namespace Shiori.Cli.Server;

/// <summary>Fans work out to isolated workspace engines and merges deterministic results.</summary>
public sealed class WorkspaceCoordinator
{
    private const int MaximumConcurrency = 8;
    private readonly IWorkspaceEngineProvider _engines;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _updateGates =
        new(StringComparer.OrdinalIgnoreCase);

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
        return new WorkspaceSearchFilesResponse(results, errors);
    }

    /// <summary>Updates selected indexes concurrently and returns after all have completed.</summary>
    public async Task<UpdateIndexesResponse> UpdateIndexesAsync(
        IReadOnlyList<string>? workspaces,
        bool force,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var paths = _engines.ResolveWorkspacePaths(workspaces);
        using var concurrency = CreateConcurrencyGate(paths.Count);
        var updates = paths.Select(path => UpdateWorkspaceAsync(
            path, force, concurrency, cancellationToken));
        var responses = await Task.WhenAll(updates).ConfigureAwait(false);
        stopwatch.Stop();
        return new UpdateIndexesResponse(
            stopwatch.ElapsedMilliseconds,
            responses.Where(response => response.Update is not null).Select(response => response.Update!).ToArray(),
            responses.Where(response => response.Error is not null).Select(response => response.Error!).ToArray());
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
                var results = engine.SearchFiles(query, limit).Select(result => new WorkspaceSearchResult(
                    info.Id, info.Name, info.Path, result.Type, result.Path,
                    result.Line, result.Snippet, result.Column)).ToArray();
                return new SearchWorkspaceResponse(results, null);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SearchWorkspaceResponse([], new WorkspaceOperationError(workspace, exception.Message));
        }
        finally
        {
            concurrency.Release();
        }
    }

    private async Task<UpdateWorkspaceResponse> UpdateWorkspaceAsync(
        string workspace,
        bool force,
        SemaphoreSlim concurrency,
        CancellationToken cancellationToken)
    {
        var updateGate = _updateGates.GetOrAdd(workspace, static _ => new SemaphoreSlim(1, 1));
        await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    var engine = _engines.GetEngine(workspace);
                    var info = engine.GetWorkspaceInfo();
                    var status = force ? engine.RebuildIndex() : engine.BuildIndex();
                    return new UpdateWorkspaceResponse(new WorkspaceIndexUpdate(info, status), null);
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                updateGate.Release();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new UpdateWorkspaceResponse(null, new WorkspaceOperationError(workspace, exception.Message));
        }
        finally
        {
            concurrency.Release();
        }
    }

    private static SemaphoreSlim CreateConcurrencyGate(int workspaceCount) =>
        new(Math.Max(1, Math.Min(workspaceCount, Math.Min(Environment.ProcessorCount, MaximumConcurrency))));

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
        WorkspaceOperationError? Error);

    private sealed record UpdateWorkspaceResponse(
        WorkspaceIndexUpdate? Update,
        WorkspaceOperationError? Error);
}
