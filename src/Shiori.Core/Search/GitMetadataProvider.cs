using System.Diagnostics;

namespace Shiori.Core.Search;

/// <summary>Reads tracked paths from the Git CLI and file timestamps from the workspace.</summary>
public sealed class GitMetadataProvider : IGitMetadataProvider
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, GitFileMetadata> GetMetadata(
        string workspaceRoot,
        IEnumerable<string> relativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(relativePaths);

        var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var paths = relativePaths
            .Select(path => TryResolve(root, path))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!.Value)
            .DistinctBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            return new Dictionary<string, GitFileMetadata>(StringComparer.OrdinalIgnoreCase);
        }

        var tracked = GetTrackedPaths(root, paths.Select(candidate => candidate.RelativePath));
        return paths.ToDictionary(
            candidate => candidate.RelativePath,
            candidate => new GitFileMetadata(
                tracked.Contains(candidate.RelativePath),
                tracked.Contains(candidate.RelativePath) && File.Exists(candidate.FullPath)
                    ? File.GetLastWriteTimeUtc(candidate.FullPath)
                    : null),
            StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetTrackedPaths(string root, IEnumerable<string> paths)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(root);
        process.StartInfo.ArgumentList.Add("ls-files");
        process.StartInfo.ArgumentList.Add("-z");
        process.StartInfo.ArgumentList.Add("--");
        foreach (var path in paths)
        {
            process.StartInfo.ArgumentList.Add(path);
        }

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(GitTimeout) || process.ExitCode != 0)
            {
                TryKill(process);
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            var output = outputTask.GetAwaiter().GetResult();
            return output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static (string RelativePath, string FullPath)? TryResolve(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return null;
        }
        var relative = NormalizePath(path);
        var fullPath = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            ? (relative, fullPath)
            : null;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
