using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Shiori.Core.Engine;
using Shiori.Core.Integration;
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
    [Description("Returns persistent index state, including indexed file and directory counts, for an allowed workspace.")]
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

    [McpServerTool(Name = "workspace_add", ReadOnly = false, Idempotent = false, OpenWorld = false)]
    [Description("Registers an existing absolute directory and starts its initial index with visible progress.")]
    public static async Task<WorkspaceIndexResponse> AddWorkspace(
        [Description("Existing absolute directory to register.")] string path,
        NativeEngineRegistry engines,
        IIndexTerminalLauncher terminalLauncher,
        CancellationToken cancellationToken = default)
    {
        var workspace = await new WorkspaceRegistry().AddAsync(path, cancellationToken).ConfigureAwait(false);
        engines.AllowWorkspace(workspace.Path);
        if (OperatingSystem.IsWindows())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentStatus = engines.GetEngine(workspace.Path).GetIndexStatus();
            terminalLauncher.Launch(workspace.Path);
            return new WorkspaceIndexResponse(workspace, currentStatus);
        }
        var status = await BuildIndexAsync(workspace.Path, engines, cancellationToken).ConfigureAwait(false);
        return new WorkspaceIndexResponse(workspace, status);
    }

    [McpServerTool(Name = "workspace_remove", ReadOnly = false, Idempotent = false, OpenWorld = false)]
    [Description("Removes a registered workspace and all of its index rows.")]
    public static async Task<WorkspaceInfo> RemoveWorkspace(
        [Description("Workspace name, ID, or absolute path.")] string identifier,
        NativeEngineRegistry engines,
        CancellationToken cancellationToken = default)
    {
        var workspace = await new WorkspaceRegistry()
            .RemoveAsync(identifier, cancellationToken)
            .ConfigureAwait(false);
        engines.DisallowWorkspace(workspace.Path);
        return workspace;
    }

    [McpServerTool(Name = "index_build", ReadOnly = false, Idempotent = false, OpenWorld = false)]
    [Description("Builds and atomically publishes the file index for a registered workspace.")]
    public static Task<IndexStatus> BuildIndex(
        [Description("Absolute path of the registered workspace.")] string workspace,
        NativeEngineRegistry engines,
        CancellationToken cancellationToken = default) =>
        BuildIndexAsync(workspace, engines, cancellationToken);

    [McpServerTool(Name = "index_rebuild", ReadOnly = false, Idempotent = false, OpenWorld = false)]
    [Description("Rebuilds and atomically replaces the file index for a registered workspace.")]
    public static Task<IndexStatus> RebuildIndex(
        [Description("Absolute path of the registered workspace.")] string workspace,
        NativeEngineRegistry engines,
        CancellationToken cancellationToken = default) =>
        BuildIndexAsync(workspace, engines, cancellationToken);

    [McpServerTool(Name = "doctor", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Runs native engine, SQLite, directory, settings, token, and workspace diagnostics.")]
    public static Task<DoctorRunner.DoctorReport> Doctor(CancellationToken cancellationToken = default) =>
        DoctorRunner.GetReportAsync(cancellationToken);

    [McpServerTool(Name = "config_claude", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Generates Claude Code MCP configuration without embedding the bearer token.")]
    public static string GenerateClaudeConfig(
        [Description("MCP server port from 1 to 65535.")] int port = 39473,
        [Description("MCP server name.")] string name = "shiori") =>
        ClaudeCodeConfigGenerator.Generate(port, name);

    [McpServerTool(Name = "config_codex", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Generates Codex MCP configuration without embedding the bearer token.")]
    public static string GenerateCodexConfig(
        [Description("MCP server port from 1 to 65535.")] int port = 39473,
        [Description("MCP server name.")] string name = "shiori") =>
        CodexConfigGenerator.Generate(port, name);

    private static async Task<IndexStatus> BuildIndexAsync(
        string workspace,
        NativeEngineRegistry engines,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = engines.GetEngine(workspace);
        var totalDirectories = await Task.Run(engine.CountIndexDirectories, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => engine.BuildIndex(totalDirectories), cancellationToken).ConfigureAwait(false);
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
