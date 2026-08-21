namespace Shiori.Core.Search;

/// <summary>Git-derived ranking signals for a workspace file.</summary>
public sealed record GitFileMetadata(bool IsTracked, DateTimeOffset? LastWriteTimeUtc);
