namespace Shiori.Core.Lsp;

/// <summary>Provides the definition-specific compatibility entry point.</summary>
public static class LspDefinitionService
{
    /// <summary>Finds definitions for a one-based source position.</summary>
    public static Task<NavigationResponse> FindAsync(
        ILspRequestRouter router,
        string workspace,
        string file,
        int line,
        int column,
        LanguageServerDescriptor? descriptor = null,
        CancellationToken cancellationToken = default) =>
        LspNavigationService.NavigateAsync(
            router,
            workspace,
            file,
            line,
            column,
            "definition",
            descriptor: descriptor,
            cancellationToken: cancellationToken);
}
