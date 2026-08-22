using System.Text.Json;
using Shiori.Native;

namespace Shiori.Cli;

/// <summary>Runs local Shiori dependency, storage, and configuration diagnostics.</summary>
internal static class DoctorRunner
{
    private const string TokenVariable = "SHIORI_MCP_TOKEN";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    internal static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var report = await GetReportAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return report.Status == "error" ? 1 : 0;
    }

    internal static async Task<DoctorReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<DoctorCheck>();
        AddNativeChecks(checks);
        AddDirectoryChecks(checks);
        AddSettingsCheck(checks);
        await AddMcpChecksAsync(checks, cancellationToken).ConfigureAwait(false);
        var status = checks.Any(check => check.Status == "error")
            ? "error"
            : checks.Any(check => check.Status == "warning") ? "warning" : "ok";
        return new DoctorReport(status, checks);
    }

    private static void AddNativeChecks(List<DoctorCheck> checks)
    {
        try
        {
            var diagnostics = NativeAbiStatus.GetDiagnostics();
            var abiStatus = diagnostics.AbiVersion == NativeAbiStatus.GetAbiVersion() ? "ok" : "error";
            checks.Add(new DoctorCheck("native_engine", abiStatus, $"ABI {diagnostics.AbiVersion}"));
            var sqliteStatus = diagnostics.Sqlite.QuickCheck == "ok" ? "ok" : "error";
            checks.Add(new DoctorCheck(
                "sqlite",
                sqliteStatus,
                $"SQLite {diagnostics.Sqlite.Version}; quick_check={diagnostics.Sqlite.QuickCheck}"));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            checks.Add(new DoctorCheck("native_engine", "error", exception.Message));
            checks.Add(new DoctorCheck("sqlite", "error", "Native SQLite diagnostics could not run."));
        }
    }

    private static void AddDirectoryChecks(List<DoctorCheck> checks)
    {
        AddDirectoryCheck(checks, "config_directory", InstallationLayout.GetDirectory("config"));
        AddDirectoryCheck(checks, "logs_directory", InstallationLayout.GetDirectory("logs"));
        AddDirectoryCheck(checks, "data_directory", WorkspaceRegistry.GetDataRoot());
    }

    private static void AddDirectoryCheck(List<DoctorCheck> checks, string name, string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probePath = Path.Combine(path, $"doctor-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
            stream.WriteByte(0);
            checks.Add(new DoctorCheck(name, "ok", path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            checks.Add(new DoctorCheck(name, "error", exception.Message));
        }
    }

    private static async Task AddMcpChecksAsync(
        List<DoctorCheck> checks,
        CancellationToken cancellationToken)
    {
        var token = Environment.GetEnvironmentVariable(TokenVariable);
        checks.Add(new DoctorCheck(
            "mcp_token",
            string.IsNullOrWhiteSpace(token) ? "warning" : token.Length >= 32 ? "ok" : "error",
            string.IsNullOrWhiteSpace(token) ? "not configured" : token.Length >= 32 ? "configured" : "must contain at least 32 characters"));

        try
        {
            var workspaces = await new WorkspaceRegistry().ListAsync(cancellationToken).ConfigureAwait(false);
            var valid = workspaces.Count > 0 && workspaces.All(workspace => Directory.Exists(workspace.Path));
            checks.Add(new DoctorCheck(
                "registered_workspaces",
                workspaces.Count == 0 ? "warning" : valid ? "ok" : "error",
                workspaces.Count == 0
                    ? "none registered"
                    : valid ? $"{workspaces.Count} registered" : "contains an unavailable directory"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            checks.Add(new DoctorCheck("registered_workspaces", "error", exception.Message));
        }
    }

    private static void AddSettingsCheck(List<DoctorCheck> checks)
    {
        try
        {
            var settings = ApplicationSettings.Load();
            checks.Add(new DoctorCheck("language", "ok", settings.Language));
        }
        catch (InvalidDataException exception)
        {
            checks.Add(new DoctorCheck("language", "error", exception.Message));
        }
    }

    internal sealed record DoctorReport(string Status, IReadOnlyList<DoctorCheck> Checks);

    internal sealed record DoctorCheck(string Name, string Status, string Detail);
}
