namespace Shiori.Core.Engine;

/// <summary>Reports one completed directory during index construction.</summary>
public sealed record IndexProgress(ulong CompletedDirectories, ulong TotalDirectories, string Path)
{
    /// <summary>Gets the integer completion percentage.</summary>
    public int Percent => TotalDirectories == 0
        ? 0
        : checked((int)(CompletedDirectories * 100 / TotalDirectories));
}
