namespace Shiori.Core.Lsp;

/// <summary>Identifies a semantic-navigation target inside the workspace.</summary>
public sealed record NavigationLocation(string Path, int Line, int Column);
