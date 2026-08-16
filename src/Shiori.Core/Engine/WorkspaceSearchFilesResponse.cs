namespace Shiori.Core.Engine;

/// <summary>Contains merged file-search results and isolated workspace failures.</summary>
public sealed record WorkspaceSearchFilesResponse(
    IReadOnlyList<WorkspaceSearchResult> Results,
    IReadOnlyList<WorkspaceOperationError> Errors);
