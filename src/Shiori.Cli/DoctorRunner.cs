using System.Text.Json;
using Shiori.Core.Lsp;
using Shiori.Native;

namespace Shiori.Cli;

/// <summary>Runs local Shiori dependency, storage, and configuration diagnostics.</summary>
internal static class DoctorRunner
{
    private const string TokenVariable = "SHIORI_MCP_TOKEN";
    private const string WorkspacesVariable = "SHIORI_ALLOWED_WORKSPACES";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    internal static int Run()
    {
        var checks = new List<DoctorCheck>();
        AddNativeChecks(checks);
        AddLspCheck(checks);
        AddDataCheck(checks);
        AddMcpChecks(checks);
        var status = checks.Any(check => check.Status == "error")
            ? "error"
            : checks.Any(check => check.Status == "warning") ? "warning" : "ok";
        Console.WriteLine(JsonSerializer.Serialize(new DoctorReport(status, checks), JsonOptions));
        return status == "error" ? 1 : 0;
    }

    private static void AddLspCheck(List<DoctorCheck> checks)
    {
        var server = CSharpLanguageServerDiscovery.Find();
        checks.Add(new DoctorCheck(
            "lsp_csharp",
            server is null ? "warning" : "ok",
            server is null
                ? $"not found; configure {CSharpLanguageServerDiscovery.PathVariable}"
                : $"{server.ExecutablePath} ({server.Source})"));
    }

    private static void AddNativeChecks(List<DoctorCheck> checks)
    {
        try
        {
            var diagnostics = NativeAbiStatus.GetDiagnostics();
            var abiStatus = diagnostics.AbiVersion == NativeAbiStatus.GetAbiVersion() ? "ok" : "error";
            checks.Add(new DoctorCheck("native_engine", abiStatus, $"ABI {diagnostics.AbiVersion}"));
            var sqliteStatus = diagnostics.Sqlite.QuickCheck == "ok" && diagnostics.Sqlite.Fts5Enabled
                ? "ok"
                : "error";
            checks.Add(new DoctorCheck(
                "sqlite",
                sqliteStatus,
                $"SQLite {diagnostics.Sqlite.Version}; quick_check={diagnostics.Sqlite.QuickCheck}; FTS5={diagnostics.Sqlite.Fts5Enabled}"));
            checks.Add(new DoctorCheck(
                "ripgrep",
                diagnostics.RipgrepAvailable ? "ok" : "error",
                diagnostics.RipgrepVersion ?? "ripgrep is unavailable"));
            checks.Add(new DoctorCheck(
                "tree_sitter",
                diagnostics.TreeSitterLanguages.Count == 9 ? "ok" : "error",
                $"Tree-sitter {diagnostics.TreeSitterVersion}; {string.Join(", ", diagnostics.TreeSitterLanguages)}"));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            checks.Add(new DoctorCheck("native_engine", "error", exception.Message));
            checks.Add(new DoctorCheck("sqlite", "error", "Native SQLite diagnostics could not run."));
            checks.Add(new DoctorCheck("ripgrep", "error", "Native ripgrep diagnostics could not run."));
            checks.Add(new DoctorCheck("tree_sitter", "error", "Native Tree-sitter diagnostics could not run."));
        }
    }

    private static void AddDataCheck(List<DoctorCheck> checks)
    {
        var dataRoot = WorkspaceRegistry.GetDataRoot();
        try
        {
            Directory.CreateDirectory(dataRoot);
            var probePath = Path.Combine(dataRoot, $"doctor-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
            stream.WriteByte(0);
            checks.Add(new DoctorCheck("data_directory", "ok", dataRoot));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            checks.Add(new DoctorCheck("data_directory", "error", exception.Message));
        }
    }

    private static void AddMcpChecks(List<DoctorCheck> checks)
    {
        var token = Environment.GetEnvironmentVariable(TokenVariable);
        checks.Add(new DoctorCheck(
            "mcp_token",
            string.IsNullOrWhiteSpace(token) ? "warning" : token.Length >= 32 ? "ok" : "error",
            string.IsNullOrWhiteSpace(token) ? "not configured" : token.Length >= 32 ? "configured" : "must contain at least 32 characters"));

        var configured = Environment.GetEnvironmentVariable(WorkspacesVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            checks.Add(new DoctorCheck("allowed_workspaces", "warning", "not configured"));
            return;
        }

        var workspaces = configured.Split(
            Path.PathSeparator,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var valid = workspaces.Length > 0 && workspaces.All(path => Path.IsPathFullyQualified(path) && Directory.Exists(path));
        checks.Add(new DoctorCheck(
            "allowed_workspaces",
            valid ? "ok" : "error",
            valid ? $"{workspaces.Length} configured" : "contains an unavailable or relative directory"));
    }

    private sealed record DoctorReport(string Status, IReadOnlyList<DoctorCheck> Checks);

    private sealed record DoctorCheck(string Name, string Status, string Detail);
}
