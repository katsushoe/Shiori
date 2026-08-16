namespace Shiori.Core.Lsp;

/// <summary>Finds a configured or installed C# language server without starting it.</summary>
public static class CSharpLanguageServerDiscovery
{
    /// <summary>Environment variable used to select a C# language-server executable.</summary>
    public const string PathVariable = "SHIORI_CSHARP_LSP_PATH";

    private static readonly string[] CandidateNames = ["csharp-ls", "OmniSharp"];

    /// <summary>Returns the first available C# language server, or <see langword="null"/>.</summary>
    public static LanguageServerDescriptor? Find(
        string? configuredPath = null,
        string? searchPath = null,
        bool? isWindows = null)
    {
        configuredPath ??= Environment.GetEnvironmentVariable(PathVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (!Path.IsPathFullyQualified(configuredPath))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(configuredPath);
            return File.Exists(fullPath)
                ? new LanguageServerDescriptor("csharp", fullPath, PathVariable)
                : null;
        }

        searchPath ??= Environment.GetEnvironmentVariable("PATH");
        var windows = isWindows ?? OperatingSystem.IsWindows();
        foreach (var directory in SplitSearchPath(searchPath))
        {
            foreach (var candidate in CandidateNames)
            {
                var executablePath = Path.Combine(directory, windows ? $"{candidate}.exe" : candidate);
                if (File.Exists(executablePath))
                {
                    return new LanguageServerDescriptor("csharp", executablePath, "PATH");
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitSearchPath(string? searchPath) =>
        string.IsNullOrWhiteSpace(searchPath)
            ? []
            : searchPath.Split(
                Path.PathSeparator,
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
