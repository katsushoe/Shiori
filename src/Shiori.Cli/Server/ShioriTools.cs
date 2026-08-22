using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Shiori.Core.Engine;
using Shiori.Core.Logging;

namespace Shiori.Cli.Server;

[McpServerToolType]
internal sealed class ShioriTools
{
    [McpServerTool(Name = "get_version", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns the running Shiori MCP server name and version.")]
    public static ServerVersionInfo GetVersion()
    {
        var version = typeof(ShioriTools).Assembly.GetName().Version?.ToString()
            ?? typeof(ShioriTools).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
        return new ServerVersionInfo("Shiori", version);
    }

    [McpServerTool(Name = "workspace_list", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists configured Shiori workspaces and their persistent SQLite databases.")]
    public static IReadOnlyList<WorkspaceInfo> ListWorkspaces(NativeEngineRegistry registry) =>
        registry.ListWorkspaces();

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

    [McpServerTool(Name = "search_files", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Searches file names and paths across one, several, or all allowed workspaces.")]
    public static async Task<WorkspaceSearchFilesResponse> SearchFiles(
        [Description("File name or relative path fragment to search for.")] string query,
        WorkspaceCoordinator coordinator,
        ILogger<ShioriTools> logger,
        [Description("Optional absolute allowed workspace.")] string? workspace = null,
        [Description("Optional absolute allowed workspace paths.")] string[]? workspaces = null,
        [Description("Maximum number of results from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var selected = MergeWorkspaceSelectors(workspace, workspaces);
        var workspaceLabel = selected is null ? null : string.Join(",", selected);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await coordinator
                .SearchFilesAsync(query, selected, limit, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            logger.LogSearchSucceeded(
                "search_files", query, workspaceLabel, response.Results.Count, stopwatch.Elapsed.TotalMilliseconds);
            if (response.Errors.Count > 0)
            {
                logger.LogSearchPartialErrors(
                    "search_files",
                    query,
                    response.Errors.Select(error => $"{error.Workspace}: {error.Message}").ToArray());
            }
            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogSearchFailed(
                "search_files", query, workspaceLabel, stopwatch.Elapsed.TotalMilliseconds, exception);
            throw;
        }
    }

    private static IReadOnlyList<string>? MergeWorkspaceSelectors(string? workspace, string[]? workspaces)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            return workspaces;
        }
        if (workspaces is null || workspaces.Length == 0)
        {
            return [workspace];
        }
        return workspaces.Append(workspace).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
