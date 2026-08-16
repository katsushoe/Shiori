namespace Shiori.Core.Engine;

/// <summary>Describes a failed operation for one selected workspace.</summary>
public sealed record WorkspaceOperationError(string Workspace, string Message);
