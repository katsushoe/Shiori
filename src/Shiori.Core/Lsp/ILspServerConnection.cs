using System.Text.Json;

namespace Shiori.Core.Lsp;

/// <summary>Represents one initialized language-server connection.</summary>
public interface ILspServerConnection : IAsyncDisposable
{
    /// <summary>Gets whether the backing language-server process is available.</summary>
    bool IsAlive { get; }

    /// <summary>Sends an LSP request.</summary>
    Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken = default);
}
