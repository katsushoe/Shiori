using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Shiori.Core.Engine;
using Shiori.Core.Logging;
using Shiori.Core.Lsp;
using Shiori.Core.Search;

namespace Shiori.Cli.Server;

[McpServerToolType]
internal sealed class ShioriTools
{
    [McpServerTool(Name = "get_version", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns the running Shiori MCP server name and version.")]
    public static ServerVersionInfo GetVersion()
    {
        var version = typeof(ShioriTools).Assembly
            .GetName().Version?.ToString()
            ?? typeof(ShioriTools).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
        return new ServerVersionInfo("Shiori", version);
    }

    [McpServerTool(Name = "search_ast", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Searches source syntax trees with a Tree-sitter query pattern.")]
    public static AstSearchResponse SearchAst(
        [Description("Tree-sitter language name, such as csharp, rust, or typescript.")] string language,
        [Description("Tree-sitter query containing one or more captures.")] string pattern,
        [Description("Absolute path of the allowed workspace.")] string workspace,
        NativeEngineRegistry registry,
        ILogger<ShioriTools> logger,
        [Description("Optional relative path prefix within the workspace.")] string? path = null,
        [Description("Maximum captures from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteSearch(
            logger, "search_ast", pattern, workspace,
            () => new AstSearchResponse(registry.GetEngine(workspace).SearchAst(language, pattern, path, limit)),
            response => response.Results.Count);
    }

    [McpServerTool(Name = "navigate", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Navigates from a source position using a lazily started language server.")]
    public static Task<NavigationResponse> Navigate(
        [Description("Navigation action: definition, references, implementations, callers, or callees.")] string action,
        [Description("Absolute path of the allowed workspace.")] string workspace,
        [Description("Relative or absolute source-file path inside the workspace.")] string file,
        [Description("One-based source line.")] int line,
        [Description("One-based source column.")] int column,
        NativeEngineRegistry registry,
        LspServerManager lspServers,
        [Description("Maximum results from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        _ = registry.GetEngine(workspace);
        return LspNavigationService.NavigateAsync(
            lspServers, workspace, file, line, column, action, limit, cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "search", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Secondary code-search tool that plans file, symbol, and text providers for one workspace.")]
    public static async Task<UnifiedSearchResponse> Search(
        [Description("Natural-language text, code identifier, filename, path, or quoted phrase.")] string query,
        [Description("Absolute path of the allowed workspace.")] string workspace,
        NativeEngineRegistry registry,
        ILogger<ShioriTools> logger,
        [Description("Optional file or directory path within the workspace.")] string? path = null,
        [Description("Maximum combined results from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteSearchAsync(
            logger, "search", query, workspace,
            () => UnifiedSearchService.SearchAsync(registry.GetEngine(workspace), query, path, limit, cancellationToken),
            result => result.Results.Count).ConfigureAwait(false);
        if (response.ProviderErrors.Count > 0)
        {
            logger.LogSearchPartialErrors(
                "search", query,
                response.ProviderErrors.Select(error => $"{error.Key}: {error.Value}").ToArray());
        }

        return response;
    }

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

    [McpServerTool(Name = "update_indexes", ReadOnly = false, Idempotent = true, OpenWorld = false)]
    [Description("Updates search databases when the user asks to update the search DB; waits for every selected workspace.")]
    public static Task<UpdateIndexesResponse> UpdateIndexes(
        WorkspaceCoordinator coordinator,
        [Description("Optional absolute allowed workspace paths; omit to update all allowed workspaces.")]
        string[]? workspaces = null,
        [Description("Force full rebuilds instead of incremental updates.")] bool force = false,
        CancellationToken cancellationToken = default) =>
        coordinator.UpdateIndexesAsync(workspaces, force, cancellationToken);

    [McpServerTool(Name = "file_outline", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Returns the indexed symbol hierarchy of one source file before reading its full contents.")]
    public static FileOutline GetFileOutline(
        [Description("Absolute path of the allowed workspace.")] string workspace,
        [Description("Relative or absolute source-file path inside the workspace.")] string path,
        NativeEngineRegistry registry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return registry.GetEngine(workspace).GetFileOutline(path);
    }

    [McpServerTool(Name = "search_files", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Primary Shiori tool for fast file-name and path discovery across one, several, or all allowed workspaces.")]
    public static async Task<WorkspaceSearchFilesResponse> SearchFiles(
        [Description("File name or relative path fragment to search for.")] string query,
        WorkspaceCoordinator coordinator,
        ILogger<ShioriTools> logger,
        [Description("Optional absolute allowed workspace; retained for single-workspace compatibility.")]
        string? workspace = null,
        [Description("Optional absolute allowed workspace paths; omit both selectors to search all allowed workspaces.")]
        string[]? workspaces = null,
        [Description("Maximum number of results from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var selected = MergeWorkspaceSelectors(workspace, workspaces);
        var workspaceLabel = selected is null ? null : string.Join(",", selected);
        var response = await ExecuteSearchAsync(
            logger, "search_files", query, workspaceLabel,
            () => coordinator.SearchFilesAsync(query, selected, limit, cancellationToken),
            result => result.Results.Count).ConfigureAwait(false);
        if (response.Errors.Count > 0)
        {
            logger.LogSearchPartialErrors(
                "search_files", query,
                response.Errors.Select(error => $"{error.Workspace}: {error.Message}").ToArray());
        }

        return response;
    }

    private static IReadOnlyList<string>? MergeWorkspaceSelectors(string? workspace, string[]? workspaces)
    {
        if (string.IsNullOrWhiteSpace(workspace)) return workspaces;
        if (workspaces is null || workspaces.Length == 0) return [workspace];
        return workspaces.Append(workspace).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    [McpServerTool(Name = "search_text", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Searches workspace file contents with ripgrep and returns bounded code locations.")]
    public static SearchFilesResponse SearchText(
        [Description("Literal text or regular expression to search for.")] string query,
        [Description("Absolute path of the allowed workspace.")] string workspace,
        NativeEngineRegistry registry,
        ILogger<ShioriTools> logger,
        [Description("Optional file or directory path within the workspace.")] string? path = null,
        [Description("Optional gitignore-style file glob, such as *.cs.")] string? glob = null,
        [Description("Treat query as a regular expression instead of literal text.")] bool regex = false,
        [Description("Use case-sensitive matching.")] bool caseSensitive = false,
        [Description("Lines before and after each match, from 0 to 10.")] int contextLines = 0,
        [Description("Maximum number of results from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteSearch(
            logger, "search_text", query, workspace,
            () =>
            {
                var engine = registry.GetEngine(workspace);
                return new SearchFilesResponse(
                    engine.SearchText(query, path, glob, regex, caseSensitive, contextLines, limit));
            },
            response => response.Results.Count);
    }

    [McpServerTool(Name = "search_symbols", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Searches indexed source symbols using SQLite FTS5 with optional filters.")]
    public static SearchSymbolsResponse SearchSymbols(
        [Description("Symbol name or qualified-name prefix to search for.")] string query,
        [Description("Absolute path of the allowed workspace.")] string workspace,
        NativeEngineRegistry registry,
        ILogger<ShioriTools> logger,
        [Description("Optional exact symbol kind, such as function or method.")] string? kind = null,
        [Description("Optional exact language name, such as rust or c_sharp.")] string? language = null,
        [Description("Optional relative path fragment.")] string? path = null,
        [Description("Maximum number of results from 1 to 100.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteSearch(
            logger, "search_symbols", query, workspace,
            () => new SearchSymbolsResponse(registry.GetEngine(workspace).SearchSymbols(query, kind, language, path, limit)),
            response => response.Results.Count);
    }

    private static T ExecuteSearch<T>(
        ILogger logger,
        string tool,
        string query,
        string? workspace,
        Func<T> execute,
        Func<T, int> countSelector)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = execute();
            stopwatch.Stop();
            logger.LogSearchSucceeded(tool, query, workspace, countSelector(result), stopwatch.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogSearchFailed(tool, query, workspace, stopwatch.Elapsed.TotalMilliseconds, exception);
            throw;
        }
    }

    private static async Task<T> ExecuteSearchAsync<T>(
        ILogger logger,
        string tool,
        string query,
        string? workspace,
        Func<Task<T>> execute,
        Func<T, int> countSelector)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await execute().ConfigureAwait(false);
            stopwatch.Stop();
            logger.LogSearchSucceeded(tool, query, workspace, countSelector(result), stopwatch.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogSearchFailed(tool, query, workspace, stopwatch.Elapsed.TotalMilliseconds, exception);
            throw;
        }
    }
}
