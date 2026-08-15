using System.ComponentModel;
using ModelContextProtocol.Server;
using Shiori.Core.Engine;

namespace Shiori.Cli.Server;

[McpServerToolType]
internal sealed class ShioriTools
{
    [McpServerTool(Name = "workspace_list", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists configured Shiori workspaces and their persistent SQLite databases.")]
    public static IReadOnlyList<WorkspaceInfo> ListWorkspaces(NativeEngineRegistry registry)
    {
        return registry.ListWorkspaces();
    }

    [McpServerTool(Name = "index_status", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns the persistent file-index state for an allowed workspace.")]
    public static IndexStatus GetIndexStatus(
        [Description("Absolute path of the allowed workspace.")] string workspace,
        NativeEngineRegistry registry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return registry.GetEngine(workspace).GetIndexStatus();
    }

    [McpServerTool(Name = "reindex", ReadOnly = false, Idempotent = true, OpenWorld = false)]
    [Description("Builds or forcibly rebuilds the persistent file index for an allowed workspace.")]
    public static IndexStatus Reindex(
        [Description("Absolute path of the allowed workspace.")] string workspace,
        NativeEngineRegistry registry,
        [Description("Force a full rescan even when the index is ready.")] bool force = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = registry.GetEngine(workspace);
        return force ? engine.RebuildIndex() : engine.BuildIndex();
    }

    [McpServerTool(Name = "search_files", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Searches file names and paths in a local Shiori workspace.")]
    public static SearchFilesResponse SearchFiles(
        [Description("File name or relative path fragment to search for.")] string query,
        [Description("Absolute path of the allowed workspace.")] string workspace,
        NativeEngineRegistry registry,
        [Description("Maximum number of results from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = registry.GetEngine(workspace);
        return new SearchFilesResponse(engine.SearchFiles(query, limit));
    }

    [McpServerTool(Name = "search_text", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Searches workspace file contents with ripgrep and returns bounded code locations.")]
    public static SearchFilesResponse SearchText(
        [Description("Literal text or regular expression to search for.")] string query,
        [Description("Absolute path of the allowed workspace.")] string workspace,
        NativeEngineRegistry registry,
        [Description("Optional file or directory path within the workspace.")] string? path = null,
        [Description("Optional gitignore-style file glob, such as *.cs.")] string? glob = null,
        [Description("Treat query as a regular expression instead of literal text.")] bool regex = false,
        [Description("Use case-sensitive matching.")] bool caseSensitive = false,
        [Description("Lines before and after each match, from 0 to 10.")] int contextLines = 0,
        [Description("Maximum number of results from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = registry.GetEngine(workspace);
        return new SearchFilesResponse(
            engine.SearchText(query, path, glob, regex, caseSensitive, contextLines, limit));
    }
}
