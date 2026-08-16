namespace Shiori.Core.Lsp;

/// <summary>Describes an installed language-server executable.</summary>
public sealed record LanguageServerDescriptor(
    string Language,
    string ExecutablePath,
    string Source,
    IReadOnlyList<string>? Arguments = null);
