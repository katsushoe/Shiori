namespace Shiori.Core.Engine;

/// <summary>Describes the persistent file-index state for one workspace.</summary>
public sealed record IndexStatus(
    string WorkspaceId,
    string Status,
    long IndexedFiles,
    long? IndexedDirectories,
    long IndexVersion,
    string? LastScan,
    string? LastFullIndex);
