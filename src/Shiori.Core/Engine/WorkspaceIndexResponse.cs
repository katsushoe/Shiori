namespace Shiori.Core.Engine;

/// <summary>Returns a registered workspace together with its published index state.</summary>
public sealed record WorkspaceIndexResponse(WorkspaceInfo Workspace, IndexStatus Index);
