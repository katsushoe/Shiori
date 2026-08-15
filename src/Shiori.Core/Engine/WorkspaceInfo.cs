namespace Shiori.Core.Engine;

/// <summary>Describes a registered workspace and its persistent index database.</summary>
public sealed record WorkspaceInfo(
    string Id,
    string Path,
    string Name,
    string DatabasePath,
    int SchemaVersion);
