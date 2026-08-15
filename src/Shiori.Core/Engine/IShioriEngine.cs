namespace Shiori.Core.Engine;

/// <summary>Defines the managed-to-native search engine boundary.</summary>
public interface IShioriEngine : IDisposable
{
    /// <summary>Gets the engine ABI version.</summary>
    uint AbiVersion { get; }

    /// <summary>Gets persistent workspace registration information.</summary>
    WorkspaceInfo GetWorkspaceInfo();

    /// <summary>Searches file names and paths in the allowed workspace.</summary>
    IReadOnlyList<SearchResult> SearchFiles(string query, int limit = 20);

    /// <summary>Searches workspace file contents through ripgrep.</summary>
    IReadOnlyList<SearchResult> SearchText(
        string query,
        string? path = null,
        string? glob = null,
        bool regex = false,
        bool caseSensitive = false,
        int contextLines = 0,
        int limit = 20);
}
