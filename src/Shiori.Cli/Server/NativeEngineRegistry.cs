using System.Collections.Concurrent;
using Shiori.Core.Engine;
using Shiori.Native;

namespace Shiori.Cli.Server;

/// <summary>Shares one native engine instance per canonical workspace.</summary>
public sealed class NativeEngineRegistry : IWorkspaceEngineProvider, IDisposable
{
    private readonly HashSet<string> _allowedWorkspaces;
    private readonly ConcurrentDictionary<string, Lazy<IShioriEngine>> _engines =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<WorkspaceIndexWatcher> _watchers;
    private bool _disposed;

    /// <summary>Initializes a registry restricted to explicit workspace roots.</summary>
    public NativeEngineRegistry(
        IEnumerable<string> allowedWorkspaces,
        Action<string, Exception>? onWatcherError = null,
        TimeSpan? watcherDebounce = null)
    {
        ArgumentNullException.ThrowIfNull(allowedWorkspaces);
        _allowedWorkspaces = allowedWorkspaces
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_allowedWorkspaces.Count == 0)
        {
            throw new ArgumentException("At least one allowed workspace is required.", nameof(allowedWorkspaces));
        }

        _watchers = _allowedWorkspaces
            .Select(path => new WorkspaceIndexWatcher(
                path,
                () => GetEngine(path),
                onWatcherError,
                watcherDebounce))
            .ToArray();
    }

    /// <summary>Gets or opens the engine for an explicitly requested workspace.</summary>
    public IShioriEngine GetEngine(string workspace)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);

        var canonicalPath = Path.GetFullPath(workspace);
        if (!_allowedWorkspaces.Contains(canonicalPath))
        {
            throw new UnauthorizedAccessException("The requested workspace is not allowed.");
        }

        var lazyEngine = _engines.GetOrAdd(
            canonicalPath,
            static path => new Lazy<IShioriEngine>(
                () => NativeShioriEngine.Open(path),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lazyEngine.Value;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ResolveWorkspacePaths(IReadOnlyList<string>? requested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var candidates = requested is null || requested.Count == 0
            ? _allowedWorkspaces
            : requested.Select(Path.GetFullPath);
        var paths = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Any(path => !_allowedWorkspaces.Contains(path)))
        {
            throw new UnauthorizedAccessException("One or more requested workspaces are not allowed.");
        }

        return paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Lists all configured workspaces and opens their persistent databases.</summary>
    public IReadOnlyList<WorkspaceInfo> ListWorkspaces()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _allowedWorkspaces
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => GetEngine(path).GetWorkspaceInfo())
            .ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        foreach (var engine in _engines.Values)
        {
            if (engine.IsValueCreated)
            {
                engine.Value.Dispose();
            }
        }

        _disposed = true;
    }
}
