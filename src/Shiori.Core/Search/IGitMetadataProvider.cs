namespace Shiori.Core.Search;

/// <summary>Provides bounded Git ranking signals for candidate workspace paths.</summary>
public interface IGitMetadataProvider
{
    /// <summary>Returns metadata keyed by normalized workspace-relative path.</summary>
    IReadOnlyDictionary<string, GitFileMetadata> GetMetadata(
        string workspaceRoot,
        IEnumerable<string> relativePaths);
}
