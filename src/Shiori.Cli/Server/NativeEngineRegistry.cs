using System.Collections.Concurrent;
using Shiori.Core.Engine;
using Shiori.Native;

namespace Shiori.Cli.Server;

/// <summary>Shares one native engine instance per canonical workspace.</summary>
public sealed class NativeEngineRegistry : IWorkspaceEngineProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, byte> _allowedWorkspaces;
    private readonly ConcurrentDictionary<string, Lazy<IShioriEngine>> _engines =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>Initializes a registry restricted to explicit workspace roots.</summary>
    public NativeEngineRegistry(IEnumerable<string> allowedWorkspaces)
    {
        ArgumentNullException.ThrowIfNull(allowedWorkspaces);
        _allowedWorkspaces = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var workspace in allowedWorkspaces)
        {
            _allowedWorkspaces.TryAdd(Path.GetFullPath(workspace), 0);
        }
    }

    /// <summary>Adds a workspace to the live MCP access boundary.</summary>
    public void AllowWorkspace(string workspace)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        _allowedWorkspaces.TryAdd(Path.GetFullPath(workspace), 0);
    }

    /// <summary>Removes a workspace from the live MCP access boundary.</summary>
    public void DisallowWorkspace(string workspace)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        _allowedWorkspaces.TryRemove(Path.GetFullPath(workspace), out _);
    }

    /// <summary>Gets or opens the engine for an explicitly requested workspace.</summary>
    public IShioriEngine GetEngine(string workspace)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);

        var canonicalPath = Path.GetFullPath(workspace);
        if (!_allowedWorkspaces.ContainsKey(canonicalPath))
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
            ? _allowedWorkspaces.Keys
            : requested.Select(Path.GetFullPath);
        var paths = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Any(path => !_allowedWorkspaces.ContainsKey(path)))
        {
            throw new UnauthorizedAccessException("One or more requested workspaces are not allowed.");
        }

        return paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Lists all configured workspaces and opens their persistent databases.</summary>
    public IReadOnlyList<WorkspaceInfo> ListWorkspaces()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _allowedWorkspaces.Keys
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
