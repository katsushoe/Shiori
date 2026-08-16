namespace Shiori.Core.Engine;

/// <summary>Represents a file-search result tagged with its source workspace.</summary>
public sealed record WorkspaceSearchResult(
    string WorkspaceId,
    string WorkspaceName,
    string WorkspacePath,
    string Type,
    string Path,
    long? Line,
    string? Snippet,
    long? Column = null);
