namespace Shiori.Core.Engine;

/// <summary>Contains completed multi-workspace index updates and isolated failures.</summary>
public sealed record UpdateIndexesResponse(
    long DurationMilliseconds,
    IReadOnlyList<WorkspaceIndexUpdate> Workspaces,
    IReadOnlyList<WorkspaceOperationError> Errors);
