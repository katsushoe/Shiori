using System.Text.Json;
using Shiori.Core.Engine;
using Shiori.Native;

namespace Shiori.Cli;

/// <summary>Persists CLI workspace registrations in the configured data directory.</summary>
internal sealed class WorkspaceRegistry
{
    private const int RegistryVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly string _registryPath;

    internal WorkspaceRegistry()
    {
        var dataRoot = GetDataRoot();
        _registryPath = Path.Combine(dataRoot, "workspaces.json");
    }

    /// <summary>Gets the configured Shiori data root.</summary>
    internal static string GetDataRoot()
    {
        return InstallationLayout.GetDataDirectory();
    }

    /// <summary>Registers an existing workspace and initializes its persistent database.</summary>
    internal WorkspaceInfo Add(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
        {
            throw new ArgumentException("Workspace path must be an existing absolute directory.", nameof(path));
        }

        using var engine = NativeShioriEngine.Open(path);
        var workspace = engine.GetWorkspaceInfo();
        var document = Read();
        var nameConflict = document.Workspaces.FirstOrDefault(item =>
            string.Equals(item.Name, workspace.Name, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.Id, workspace.Id, StringComparison.Ordinal));
        if (nameConflict is not null)
        {
            throw new InvalidOperationException(
                $"Workspace name '{workspace.Name}' is already registered for {nameConflict.Path}.");
        }

        document.Workspaces.RemoveAll(item => string.Equals(item.Id, workspace.Id, StringComparison.Ordinal));
        document.Workspaces.Add(workspace);
        Sort(document.Workspaces);
        Write(document);
        return workspace;
    }

    /// <summary>Lists registered workspaces in stable name and path order.</summary>
    internal IReadOnlyList<WorkspaceInfo> List()
    {
        var document = Read();
        Sort(document.Workspaces);
        return document.Workspaces;
    }

    /// <summary>Removes a workspace registration without deleting its index database.</summary>
    internal WorkspaceInfo Remove(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var document = Read();
        var fullPath = Path.IsPathFullyQualified(identifier) ? Path.GetFullPath(identifier) : null;
        var workspace = document.Workspaces.FirstOrDefault(item =>
            string.Equals(item.Id, identifier, StringComparison.Ordinal) ||
            string.Equals(item.Name, identifier, StringComparison.OrdinalIgnoreCase) ||
            fullPath is not null && string.Equals(item.Path, NormalizePath(fullPath), PathComparison));
        if (workspace is null)
        {
            throw new InvalidOperationException($"Workspace is not registered: {identifier}");
        }

        document.Workspaces.Remove(workspace);
        Write(document);
        return workspace;
    }

    private RegistryDocument Read()
    {
        if (!File.Exists(_registryPath))
        {
            return new RegistryDocument(RegistryVersion, []);
        }

        var document = JsonSerializer.Deserialize<RegistryDocument>(File.ReadAllText(_registryPath), JsonOptions)
            ?? throw new InvalidOperationException("Workspace registry is empty or invalid.");
        if (document.Version != RegistryVersion)
        {
            throw new InvalidOperationException(
                $"Workspace registry version {document.Version} is not supported.");
        }

        return document;
    }

    private void Write(RegistryDocument document)
    {
        var directory = Path.GetDirectoryName(_registryPath)
            ?? throw new InvalidOperationException("Workspace registry directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"workspaces.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, _registryPath, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void Sort(List<WorkspaceInfo> workspaces)
    {
        workspaces.Sort(static (left, right) =>
        {
            var nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return nameComparison != 0
                ? nameComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
        });
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return OperatingSystem.IsWindows() ? normalized.ToLowerInvariant() : normalized;
    }

    private sealed record RegistryDocument(int Version, List<WorkspaceInfo> Workspaces);
}
