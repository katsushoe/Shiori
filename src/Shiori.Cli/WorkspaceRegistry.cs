using System.Text.Json;
using Microsoft.Data.Sqlite;
using Shiori.Core.Engine;
using Shiori.Native;

namespace Shiori.Cli;

/// <summary>Persists registered workspaces and all file indexes in one SQLite database.</summary>
internal sealed class WorkspaceRegistry
{
    private const int SchemaVersion = 3;
    private const string DatabaseFileName = "shiori.db";
    private const string LegacyRegistryFileName = "workspaces.json";
    private const string LegacySqliteRegistryFileName = "workspaces.db";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly string _dataRoot;
    private readonly string _databasePath;
    private readonly string _legacyRegistryPath;
    private readonly string _legacySqliteRegistryPath;
    private readonly string _legacyIndexesPath;

    internal WorkspaceRegistry(string? dataRoot = null)
    {
        _dataRoot = Path.GetFullPath(dataRoot ?? GetDataRoot());
        _databasePath = Path.Combine(_dataRoot, DatabaseFileName);
        _legacyRegistryPath = Path.Combine(_dataRoot, LegacyRegistryFileName);
        _legacySqliteRegistryPath = Path.Combine(_dataRoot, LegacySqliteRegistryFileName);
        _legacyIndexesPath = Path.Combine(_dataRoot, "indexes");
    }

    /// <summary>Gets the configured Shiori data root.</summary>
    internal static string GetDataRoot() => InstallationLayout.GetDataDirectory();

    /// <summary>Gets the unified Shiori database path.</summary>
    internal string DatabasePath => _databasePath;

    /// <summary>Registers an existing workspace in the unified database.</summary>
    internal async Task<WorkspaceInfo> AddAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
        {
            throw new ArgumentException("Workspace path must be an existing absolute directory.", nameof(path));
        }

        await using (var connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
        {
        }

        using var engine = NativeShioriEngine.Open(path);
        return engine.GetWorkspaceInfo();
    }

    /// <summary>Lists registered workspaces in stable name and path order.</summary>
    internal async Task<IReadOnlyList<WorkspaceInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, path, name
            FROM Workspaces
            ORDER BY name COLLATE NOCASE, path COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var workspaces = new List<WorkspaceInfo>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            workspaces.Add(ReadWorkspace(reader));
        }

        return workspaces;
    }

    /// <summary>Removes one workspace and its index rows from the unified database.</summary>
    internal async Task<WorkspaceInfo> RemoveAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var workspace = await FindAsync(connection, transaction, identifier, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workspace is not registered: {identifier}");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Workspaces WHERE id = $id;";
        command.Parameters.AddWithValue("$id", workspace.Id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return workspace;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataRoot);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task InitializeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText = "PRAGMA user_version;";
            var currentVersion = Convert.ToInt32(
                await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture);
            if (currentVersion > SchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unified database schema {currentVersion} is newer than supported {SchemaVersion}.");
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;
                CREATE TABLE IF NOT EXISTS Workspaces (
                    id TEXT PRIMARY KEY,
                    path TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    last_indexed_at TEXT
                );
                DROP INDEX IF EXISTS IX_Workspaces_Name_NoCase;
                CREATE TABLE IF NOT EXISTS index_state_v2 (
                    workspace_id TEXT PRIMARY KEY REFERENCES Workspaces(id) ON DELETE CASCADE,
                    active_generation TEXT,
                    index_version INTEGER NOT NULL,
                    last_scan TEXT,
                    last_full_index TEXT,
                    status TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS files_v2 (
                    workspace_id TEXT NOT NULL REFERENCES Workspaces(id) ON DELETE CASCADE,
                    generation_id TEXT NOT NULL,
                    path TEXT NOT NULL,
                    relative_path TEXT NOT NULL,
                    file_name TEXT NOT NULL,
                    extension TEXT,
                    size INTEGER NOT NULL,
                    mtime INTEGER NOT NULL,
                    indexed_at TEXT NOT NULL,
                    PRIMARY KEY(workspace_id, generation_id, relative_path)
                );
                CREATE INDEX IF NOT EXISTS files_v2_search
                    ON files_v2(workspace_id, generation_id, relative_path COLLATE NOCASE);
                PRAGMA user_version = 3;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await MigrateLegacySqliteRegistryAsync(connection, cancellationToken).ConfigureAwait(false);
        await MigrateLegacyJsonRegistryAsync(connection, cancellationToken).ConfigureAwait(false);
        await MigrateWorkspaceDatabasesAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task MigrateLegacySqliteRegistryAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_legacySqliteRegistryPath))
        {
            return;
        }

        await AttachAsync(connection, _legacySqliteRegistryPath, cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(
                connection,
                """
                INSERT OR IGNORE INTO Workspaces (id, path, name, created_at, updated_at)
                SELECT Id, Path, Name, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                       strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                FROM legacy.Workspaces;
                INSERT OR IGNORE INTO index_state_v2 (workspace_id, index_version, status)
                SELECT Id, 0, 'not_indexed' FROM legacy.Workspaces;
                """,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DetachAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        File.Move(_legacySqliteRegistryPath, $"{_legacySqliteRegistryPath}.migrated", true);
    }

    private async Task MigrateLegacyJsonRegistryAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_legacyRegistryPath))
        {
            return;
        }

        var json = await File.ReadAllTextAsync(_legacyRegistryPath, cancellationToken).ConfigureAwait(false);
        var document = JsonSerializer.Deserialize<LegacyRegistryDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Legacy workspace registry is empty or invalid.");
        if (document.Version != 1)
        {
            throw new InvalidOperationException(
                $"Legacy workspace registry version {document.Version} is not supported.");
        }

        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var workspace in document.Workspaces)
        {
            await InsertWorkspaceAsync(connection, transaction, workspace, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        File.Move(_legacyRegistryPath, $"{_legacyRegistryPath}.migrated", true);
    }

    private async Task MigrateWorkspaceDatabasesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_legacyIndexesPath))
        {
            return;
        }

        var legacyDatabases = Directory.EnumerateFiles(
            _legacyIndexesPath,
            "shiori.db",
            SearchOption.AllDirectories).ToArray();
        foreach (var legacyPath in legacyDatabases)
        {
            await MigrateWorkspaceDatabaseAsync(connection, legacyPath, cancellationToken).ConfigureAwait(false);
            var directory = Path.GetDirectoryName(legacyPath)
                ?? throw new InvalidOperationException("Legacy workspace index directory is unavailable.");
            Directory.Delete(directory, true);
        }

        if (!Directory.EnumerateFileSystemEntries(_legacyIndexesPath).Any())
        {
            Directory.Delete(_legacyIndexesPath);
        }
    }

    private static async Task MigrateWorkspaceDatabaseAsync(
        SqliteConnection connection,
        string legacyPath,
        CancellationToken cancellationToken)
    {
        await AttachAsync(connection, legacyPath, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await ExecuteAsync(
                connection,
                """
                INSERT OR REPLACE INTO Workspaces
                    (id, path, name, created_at, updated_at, last_indexed_at)
                SELECT id, path, name, created_at, updated_at, last_indexed_at
                FROM legacy.workspaces;
                INSERT OR REPLACE INTO index_state_v2
                    (workspace_id, active_generation, index_version, last_scan, last_full_index, status)
                SELECT workspace_id, active_generation, index_version, last_scan, last_full_index, status
                FROM legacy.index_state_v2;
                INSERT OR REPLACE INTO files_v2
                    (workspace_id, generation_id, path, relative_path, file_name, extension,
                     size, mtime, indexed_at)
                SELECT workspace_id, generation_id, path, relative_path, file_name, extension,
                       size, mtime, indexed_at
                FROM legacy.files_v2;
                """,
                cancellationToken,
                transaction).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DetachAsync(connection, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<WorkspaceInfo?> FindAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string identifier,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.IsPathFullyQualified(identifier) ? NormalizePath(Path.GetFullPath(identifier)) : null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, path, name
            FROM Workspaces
            WHERE id = $identifier
               OR name = $identifier COLLATE NOCASE
               OR ($path IS NOT NULL AND path = $path COLLATE NOCASE)
            LIMIT 2;
            """;
        command.Parameters.AddWithValue("$identifier", identifier);
        command.Parameters.AddWithValue("$path", (object?)fullPath ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var workspace = ReadWorkspace(reader);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Workspace identifier is ambiguous; use an ID or absolute path: {identifier}");
        }

        return workspace;
    }

    private static async Task InsertWorkspaceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        WorkspaceInfo workspace,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO Workspaces (id, path, name, created_at, updated_at)
            VALUES ($id, $path, $name, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                    strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            INSERT OR IGNORE INTO index_state_v2 (workspace_id, index_version, status)
            VALUES ($id, 0, 'not_indexed');
            """;
        command.Parameters.AddWithValue("$id", workspace.Id);
        command.Parameters.AddWithValue("$path", workspace.Path);
        command.Parameters.AddWithValue("$name", workspace.Name);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task AttachAsync(
        SqliteConnection connection,
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "ATTACH DATABASE $path AS legacy;";
        command.Parameters.AddWithValue("$path", databasePath);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DetachAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, "DETACH DATABASE legacy;", cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private WorkspaceInfo ReadWorkspace(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        _databasePath,
        SchemaVersion);

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return OperatingSystem.IsWindows() ? normalized.ToLowerInvariant() : normalized;
    }

    private sealed record LegacyRegistryDocument(int Version, List<WorkspaceInfo> Workspaces);
}
