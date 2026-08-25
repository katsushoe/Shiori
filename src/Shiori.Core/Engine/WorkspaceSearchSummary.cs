namespace Shiori.Core.Engine;

/// <summary>Summarizes one workspace included in a file-search request.</summary>
public sealed record WorkspaceSearchSummary(
    string WorkspaceId,
    string WorkspaceName,
    string WorkspacePath,
    long SearchTargetDirectories,
    long SearchTargetFiles,
    string SearchResult,
    int HitCount,
    string IndexStatus,
    string? ActionRequired,
    string? SuggestedTool,
    string? Message);
