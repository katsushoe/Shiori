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
}
