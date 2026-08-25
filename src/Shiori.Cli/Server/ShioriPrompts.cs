using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Shiori.Cli.Server;

/// <summary>
/// Provides MCP guidance for discovering and using Shiori.
/// </summary>
internal sealed class ShioriPrompts
{
    internal const string ServerInstructions = """
        Shiori is a local-first file discovery server for AI agents. Use it to quickly locate files by file name, path, and indexed metadata across explicitly registered workspaces. Shiori does not read or search file contents.

        Start with workspace_list when the search scope is unclear. Use search_files for structured results or search for a concise cross-workspace summary. Narrow the request with workspace selectors when a workspace is known. Use index_status when results may be stale. Workspace and index mutations change local state or the filesystem access boundary, so perform them only when the user requests that change. Use the shiori_guide prompt for a complete usage guide.
        """;

    /// <summary>
    /// Returns a practical guide to Shiori's purpose, search workflow, and safety boundaries.
    /// </summary>
    [McpServerPrompt(Name = "shiori_guide", Title = "How to use Shiori")]
    [Description("Explains Shiori's purpose, recommended workflow, limitations, and safe use of its MCP tools.")]
    public static string GetGuide() => """
        Use Shiori when you need to find where a local file is located without scanning file contents.

        Recommended workflow:
        1. Call workspace_list to see which local directories are registered and available.
        2. Call search_files with words from the expected file name or path. Omit workspace selectors to search every registered workspace, or specify one or more selectors to narrow the scope.
        3. Use search when you want a compact summary instead of structured file results.
        4. Call index_status if expected files are missing or results may be stale.
        5. After locating a file, use an appropriate filesystem tool to read its contents; Shiori only indexes file names, paths, and metadata.

        Administration:
        - workspace_add registers a directory and starts its initial index.
        - workspace_remove removes the registration and its index rows, not the source directory.
        - index_build and index_rebuild update a registered workspace's index.
        - doctor reports configuration, native engine, SQLite, token, and workspace diagnostics.

        Safety boundaries:
        Registered workspaces define Shiori's local filesystem access boundary. Adding or removing a workspace and rebuilding an index change local state, so do so only when requested. Search tools are read-only. Shiori never searches inside file contents.
        """;
}
