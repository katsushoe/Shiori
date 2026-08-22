namespace Shiori.Core.Engine;

/// <summary>Defines the managed-to-native search engine boundary.</summary>
public interface IShioriEngine : IDisposable
{
    /// <summary>Gets the engine ABI version.</summary>
    uint AbiVersion { get; }

    /// <summary>Gets persistent workspace registration information.</summary>
    WorkspaceInfo GetWorkspaceInfo();

    /// <summary>Gets the current persistent file-index state.</summary>
    IndexStatus GetIndexStatus();

    /// <summary>Counts directories included by the workspace index rules.</summary>
    ulong CountIndexDirectories();

    /// <summary>Builds and atomically publishes the file index.</summary>
    IndexStatus BuildIndex(ulong totalDirectories, Action<IndexProgress>? progress = null);

    /// <summary>Searches file names and paths in the allowed workspace.</summary>
    IReadOnlyList<SearchResult> SearchFiles(string query, int limit = 20);
}
