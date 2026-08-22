using System.Text.Json;
using Microsoft.Data.Sqlite;
using Shiori.Cli;
using Shiori.Core.Engine;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class WorkspaceRegistryTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"shiori-registry-{Guid.NewGuid():N}");

    [Fact]
    public async Task ListAsync_CreatesUnifiedDatabaseSchema()
    {
        var registry = new WorkspaceRegistry(_dataRoot);

        var workspaces = await registry.ListAsync(CancellationToken.None);

        Assert.Empty(workspaces);
        Assert.Equal(Path.Combine(_dataRoot, "shiori.db"), registry.DatabasePath);
        await using var connection = await OpenAsync(registry.DatabasePath);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN " +
            "('Workspaces', 'index_state_v2', 'files_v2', 'index_directory_progress');";
        Assert.Equal(4L, await command.ExecuteScalarAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_MigratesLegacyJsonRegistry()
    {
        Directory.CreateDirectory(_dataRoot);
        var workspace = CreateWorkspace("legacy");
        var json = JsonSerializer.Serialize(new { version = 1, workspaces = new[] { workspace } }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
        var legacyPath = Path.Combine(_dataRoot, "workspaces.json");
        await File.WriteAllTextAsync(legacyPath, json, CancellationToken.None);
        var registry = new WorkspaceRegistry(_dataRoot);

        var workspaces = await registry.ListAsync(CancellationToken.None);

        Assert.Equal([workspace with { DatabasePath = registry.DatabasePath, SchemaVersion = 4 }], workspaces);
        Assert.False(File.Exists(legacyPath));
        Assert.True(File.Exists($"{legacyPath}.migrated"));
    }

    [Fact]
    public async Task ListAsync_MigratesPerWorkspaceDatabaseAndDeletesLegacyDirectory()
    {
        var workspace = CreateWorkspace("indexed");
        var legacyPath = Path.Combine(_dataRoot, "indexes", workspace.Id, "shiori.db");
        await CreateLegacyIndexAsync(legacyPath, workspace);
        var registry = new WorkspaceRegistry(_dataRoot);

        var workspaces = await registry.ListAsync(CancellationToken.None);

        Assert.Equal([workspace with { DatabasePath = registry.DatabasePath, SchemaVersion = 4 }], workspaces);
        Assert.False(Directory.Exists(Path.Combine(_dataRoot, "indexes")));
        await using var connection = await OpenAsync(registry.DatabasePath);
        Assert.Equal(1L, await CountAsync(connection, "files_v2"));
        Assert.Equal(1L, await CountAsync(connection, "index_state_v2"));
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlySelectedWorkspaceRows()
    {
        var registry = new WorkspaceRegistry(_dataRoot);
        _ = await registry.ListAsync(CancellationToken.None);
        var removedWorkspace = CreateWorkspace("remove");
        var retainedWorkspace = CreateWorkspace("retain");
        await InsertIndexedWorkspaceAsync(registry.DatabasePath, removedWorkspace);
        await InsertIndexedWorkspaceAsync(registry.DatabasePath, retainedWorkspace);

        var removed = await registry.RemoveAsync(removedWorkspace.Id, CancellationToken.None);

        Assert.Equal(removedWorkspace with { DatabasePath = registry.DatabasePath, SchemaVersion = 4 }, removed);
        Assert.Equal(
            [retainedWorkspace with { DatabasePath = registry.DatabasePath, SchemaVersion = 4 }],
            await registry.ListAsync(CancellationToken.None));
        await using var connection = await OpenAsync(registry.DatabasePath);
        Assert.Equal(1L, await CountAsync(connection, "files_v2"));
        Assert.Equal(1L, await CountAsync(connection, "index_state_v2"));
    }

    [Fact]
    public async Task RemoveAsync_WhenNameIsAmbiguous_RejectsWithoutDeletingRows()
    {
        var registry = new WorkspaceRegistry(_dataRoot);
        _ = await registry.ListAsync(CancellationToken.None);
        var first = CreateWorkspace("first") with { Name = "shared" };
        var second = CreateWorkspace("second") with { Name = "shared" };
        await InsertIndexedWorkspaceAsync(registry.DatabasePath, first);
        await InsertIndexedWorkspaceAsync(registry.DatabasePath, second);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.RemoveAsync("shared", CancellationToken.None));

        Assert.Equal(2, (await registry.ListAsync(CancellationToken.None)).Count);
        await using var connection = await OpenAsync(registry.DatabasePath);
        Assert.Equal(2L, await CountAsync(connection, "files_v2"));
    }

    [Fact]
    public async Task ListInterruptedAsync_ReturnsOnlyUnpublishedIndexGenerations()
    {
        var registry = new WorkspaceRegistry(_dataRoot);
        _ = await registry.ListAsync(CancellationToken.None);
        var interrupted = CreateWorkspace("interrupted");
        var ready = CreateWorkspace("ready");
        await InsertIndexedWorkspaceAsync(registry.DatabasePath, interrupted);
        await InsertIndexedWorkspaceAsync(registry.DatabasePath, ready);
        await using (var connection = await OpenAsync(registry.DatabasePath))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                UPDATE index_state_v2
                SET status = 'indexing', staging_generation = 'staging',
                    staging_total_directories = 2, staging_completed_directories = 1
                WHERE workspace_id = $id;
                """;
            command.Parameters.AddWithValue("$id", interrupted.Id);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var result = await registry.ListInterruptedAsync(CancellationToken.None);

        Assert.Equal(
            [interrupted with { DatabasePath = registry.DatabasePath, SchemaVersion = 4 }],
            result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, true);
        }

        GC.SuppressFinalize(this);
    }

    private WorkspaceInfo CreateWorkspace(string id) => new(
        id,
        $"c:/workspace/{id}",
        id,
        Path.Combine(_dataRoot, "indexes", id, "shiori.db"),
        2);

    private static async Task<SqliteConnection> OpenAsync(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(CancellationToken.None);
        return connection;
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table};";
        return (long)(await command.ExecuteScalarAsync(CancellationToken.None) ?? 0L);
    }

    private static async Task CreateLegacyIndexAsync(string databasePath, WorkspaceInfo workspace)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = await OpenAsync(databasePath);
        await CreateLegacySchemaAsync(connection);
        await InsertIndexedWorkspaceAsync(connection, workspace);
    }

    private static async Task InsertIndexedWorkspaceAsync(string databasePath, WorkspaceInfo workspace)
    {
        await using var connection = await OpenAsync(databasePath);
        await InsertIndexedWorkspaceAsync(connection, workspace);
    }

    private static async Task CreateLegacySchemaAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE workspaces (
                id TEXT PRIMARY KEY, path TEXT NOT NULL UNIQUE, name TEXT NOT NULL,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL, last_indexed_at TEXT);
            CREATE TABLE index_state_v2 (
                workspace_id TEXT PRIMARY KEY REFERENCES workspaces(id) ON DELETE CASCADE,
                active_generation TEXT, index_version INTEGER NOT NULL, last_scan TEXT,
                last_full_index TEXT, status TEXT NOT NULL);
            CREATE TABLE files_v2 (
                workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                generation_id TEXT NOT NULL, path TEXT NOT NULL, relative_path TEXT NOT NULL,
                file_name TEXT NOT NULL, extension TEXT, size INTEGER NOT NULL,
                mtime INTEGER NOT NULL, indexed_at TEXT NOT NULL,
                PRIMARY KEY(workspace_id, generation_id, relative_path));
            """;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task InsertIndexedWorkspaceAsync(SqliteConnection connection, WorkspaceInfo workspace)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Workspaces (id, path, name, created_at, updated_at)
            VALUES ($id, $path, $name, 'now', 'now');
            INSERT INTO index_state_v2
                (workspace_id, active_generation, index_version, status)
            VALUES ($id, 'active', 1, 'ready');
            INSERT INTO files_v2
                (workspace_id, generation_id, path, relative_path, file_name,
                 extension, size, mtime, indexed_at)
            VALUES ($id, 'active', $filePath, 'file.txt', 'file.txt', '.txt', 1, 1, 'now');
            """;
        command.Parameters.AddWithValue("$id", workspace.Id);
        command.Parameters.AddWithValue("$path", workspace.Path);
        command.Parameters.AddWithValue("$name", workspace.Name);
        command.Parameters.AddWithValue("$filePath", $"{workspace.Path}/file.txt");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
