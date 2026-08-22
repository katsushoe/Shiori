namespace Shiori.Cli;

internal static class IndexPathFormatter
{
    internal static string FormatAbsolute(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + fullPath[8..];
        }
        return fullPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
            ? fullPath[4..]
            : fullPath;
    }
}
