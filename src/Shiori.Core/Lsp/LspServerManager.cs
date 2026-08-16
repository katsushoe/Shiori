using System.Collections.Concurrent;
using System.Text.Json;

namespace Shiori.Core.Lsp;

/// <summary>Lazily starts, shares, and recovers language-server connections.</summary>
public sealed class LspServerManager : IAsyncDisposable
{
    private readonly ILspServerConnectionFactory _factory;
    private readonly ConcurrentDictionary<string, Lazy<Task<ILspServerConnection>>> _connections =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>Initializes a language-server manager.</summary>
    public LspServerManager(ILspServerConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>Sends a request through a shared workspace connection.</summary>
    public async Task<JsonElement> SendRequestAsync(
        LanguageServerDescriptor descriptor,
        string workspace,
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var canonicalWorkspace = Path.GetFullPath(workspace);
        if (!Directory.Exists(canonicalWorkspace))
        {
            throw new DirectoryNotFoundException($"Workspace is unavailable: {canonicalWorkspace}");
        }

        var key = $"{descriptor.Language}\0{descriptor.ExecutablePath}\0{canonicalWorkspace}";
        var connection = await GetConnectionAsync(key, descriptor, canonicalWorkspace, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await connection.SendRequestAsync(method, parameters, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or ObjectDisposedException)
        {
            await RemoveConnectionAsync(key).ConfigureAwait(false);
            connection = await GetConnectionAsync(key, descriptor, canonicalWorkspace, cancellationToken)
                .ConfigureAwait(false);
            return await connection.SendRequestAsync(method, parameters, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var connections = _connections.Keys.Select(RemoveConnectionAsync).ToArray();
        await Task.WhenAll(connections).ConfigureAwait(false);
    }

    private async Task<ILspServerConnection> GetConnectionAsync(
        string key,
        LanguageServerDescriptor descriptor,
        string workspace,
        CancellationToken cancellationToken)
    {
        var lazy = _connections.GetOrAdd(
            key,
            _ => new Lazy<Task<ILspServerConnection>>(
                () => _factory.StartAsync(descriptor, workspace, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var connection = await lazy.Value.ConfigureAwait(false);
            if (connection.IsAlive) return connection;
            await RemoveConnectionAsync(key).ConfigureAwait(false);
            return await GetConnectionAsync(key, descriptor, workspace, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            _connections.TryRemove(new KeyValuePair<string, Lazy<Task<ILspServerConnection>>>(key, lazy));
            throw;
        }
    }

    private async Task RemoveConnectionAsync(string key)
    {
        if (!_connections.TryRemove(key, out var lazy) || !lazy.IsValueCreated) return;
        var connection = await lazy.Value.ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
    }
}
