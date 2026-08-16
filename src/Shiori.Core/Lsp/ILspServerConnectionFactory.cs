namespace Shiori.Core.Lsp;

/// <summary>Creates initialized language-server connections.</summary>
public interface ILspServerConnectionFactory
{
    /// <summary>Starts and initializes a server for one workspace.</summary>
    Task<ILspServerConnection> StartAsync(
        LanguageServerDescriptor descriptor,
        string workspace,
        CancellationToken cancellationToken = default);
}
