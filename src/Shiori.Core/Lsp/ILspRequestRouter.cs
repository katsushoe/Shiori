using System.Text.Json;

namespace Shiori.Core.Lsp;

/// <summary>Routes semantic requests through workspace language-server connections.</summary>
public interface ILspRequestRouter
{
    /// <summary>Sends an LSP request for one workspace.</summary>
    Task<JsonElement> SendRequestAsync(
        LanguageServerDescriptor descriptor,
        string workspace,
        string method,
        object? parameters,
        CancellationToken cancellationToken = default);
}
