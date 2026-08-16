namespace Shiori.Core.Engine;

/// <summary>Contains the completed index state for one workspace.</summary>
public sealed record WorkspaceIndexUpdate(WorkspaceInfo Workspace, IndexStatus Status);
