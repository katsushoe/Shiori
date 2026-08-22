use crate::index::{self, IndexedFile, ScanEvent};
use rusqlite::{Connection, params};
use serde::Serialize;
use sha2::{Digest, Sha256};
use std::collections::HashSet;
use std::fmt::Write;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

const SCHEMA_VERSION: i64 = 4;
const BATCH_FILE_LIMIT: usize = 1_000;
const BATCH_BYTE_LIMIT: usize = 16 * 1024 * 1024;
const BATCH_TIME_LIMIT: Duration = Duration::from_secs(2);

pub struct WorkspaceDatabase {
    connection: Mutex<Connection>,
    info: WorkspaceInfo,
}

pub struct WorkspaceInfo {
    pub id: String,
    pub path: String,
    pub name: String,
    pub database_path: String,
    pub schema_version: i64,
}

#[derive(Serialize)]
pub struct IndexStatus {
    pub workspace_id: String,
    pub status: String,
    pub indexed_files: i64,
    pub index_version: i64,
    pub last_scan: Option<String>,
    pub last_full_index: Option<String>,
}

#[derive(Serialize)]
pub struct SqliteDiagnostics {
    pub version: String,
    pub quick_check: String,
}

impl WorkspaceDatabase {
    pub fn open(root: &Path) -> Result<Self, String> {
        let data_root = platform_data_root()?;
        Self::open_at(root, &data_root)
    }

    fn open_at(root: &Path, data_root: &Path) -> Result<Self, String> {
        let normalized_path = normalize_path(root);
        let workspace_id = workspace_id(&normalized_path);
        std::fs::create_dir_all(data_root)
            .map_err(|source| format!("cannot create data directory: {source}"))?;
        let database_path = data_root.join("shiori.db");
        let mut connection = Connection::open(&database_path)
            .map_err(|source| format!("cannot open workspace database: {source}"))?;
        configure(&connection)?;
        migrate(&mut connection)?;
        let name = root
            .file_name()
            .and_then(|value| value.to_str())
            .unwrap_or(&normalized_path)
            .to_owned();
        register_workspace(&connection, &workspace_id, &normalized_path, &name)?;
        Ok(Self {
            connection: Mutex::new(connection),
            info: WorkspaceInfo {
                id: workspace_id,
                path: normalized_path,
                name,
                database_path: database_path.to_string_lossy().into_owned(),
                schema_version: SCHEMA_VERSION,
            },
        })
    }

    pub fn info(&self) -> &WorkspaceInfo {
        &self.info
    }

    pub fn validate(&self) -> Result<(), String> {
        let connection = self.lock()?;
        let result: String = connection
            .query_row("PRAGMA quick_check", [], |row| row.get(0))
            .map_err(|source| format!("workspace database validation failed: {source}"))?;
        if result != "ok" {
            return Err(format!("workspace database validation failed: {result}"));
        }
        Ok(())
    }

    pub fn build_index(
        &self,
        root: &Path,
        total_directories: u64,
        mut progress: impl FnMut(u64, u64, &str),
    ) -> Result<IndexStatus, String> {
        let mut connection = self.lock()?;
        let (generation, completed_directory_paths) =
            prepare_index(&mut connection, &self.info.id, total_directories)?;
        let mut batch = Vec::<IndexedFile>::with_capacity(BATCH_FILE_LIMIT);
        let mut batch_bytes = 0_usize;
        let mut last_flush = Instant::now();
        let mut completed_directories = completed_directory_paths.len() as u64;

        let scan_result = index::scan(root, &completed_directory_paths, |event| {
            match event {
                ScanEvent::File(file) => {
                    batch_bytes = batch_bytes.saturating_add(file.estimated_bytes());
                    batch.push(file);
                    if batch.len() >= BATCH_FILE_LIMIT
                        || batch_bytes >= BATCH_BYTE_LIMIT
                        || last_flush.elapsed() >= BATCH_TIME_LIMIT
                    {
                        flush_batch(&mut connection, &self.info.id, &generation, &mut batch)?;
                        batch_bytes = 0;
                        last_flush = Instant::now();
                    }
                }
                ScanEvent::DirectoryComplete(path) => {
                    checkpoint_directory(
                        &mut connection,
                        &self.info.id,
                        &generation,
                        &path,
                        &mut batch,
                    )?;
                    batch_bytes = 0;
                    last_flush = Instant::now();
                    completed_directories = completed_directories.saturating_add(1);
                    progress(completed_directories, total_directories, &path);
                }
            }
            Ok(())
        });
        scan_result?;
        flush_batch(&mut connection, &self.info.id, &generation, &mut batch)?;
        if completed_directories != total_directories {
            return Err(format!(
                "directory count changed during indexing: expected {total_directories}, completed {completed_directories}"
            ));
        }
        publish_generation(&mut connection, &self.info.id, &generation)?;
        read_index_status(&connection, &self.info.id)
    }

    pub fn index_status(&self) -> Result<IndexStatus, String> {
        let connection = self.lock()?;
        read_index_status(&connection, &self.info.id)
    }

    pub fn search_files(&self, query: &str, limit: usize) -> Result<Vec<PathBuf>, String> {
        let connection = self.lock()?;
        let pattern = format!("%{}%", escape_like(query));
        let mut statement = connection
            .prepare(
                "SELECT files.relative_path
                 FROM files_v2 AS files
                 JOIN index_state_v2 AS state
                   ON state.workspace_id = files.workspace_id
                  AND state.active_generation = files.generation_id
                 WHERE files.workspace_id = ?1
                   AND files.relative_path LIKE ?2 ESCAPE '\\'
                 ORDER BY files.relative_path COLLATE NOCASE
                 LIMIT ?3",
            )
            .map_err(|source| format!("cannot prepare file search: {source}"))?;
        let rows = statement
            .query_map(params![self.info.id, pattern, limit as i64], |row| {
                row.get::<_, String>(0)
            })
            .map_err(|source| format!("cannot search file index: {source}"))?;
        rows.map(|row| {
            row.map(PathBuf::from)
                .map_err(|source| format!("cannot read file search result: {source}"))
        })
        .collect()
    }

    fn lock(&self) -> Result<std::sync::MutexGuard<'_, Connection>, String> {
        self.connection
            .lock()
            .map_err(|_| "workspace database lock is poisoned".to_owned())
    }
}

fn flush_batch(
    connection: &mut Connection,
    workspace_id: &str,
    generation: &str,
    batch: &mut Vec<IndexedFile>,
) -> Result<(), String> {
    if batch.is_empty() {
        return Ok(());
    }
    let transaction = connection
        .transaction()
        .map_err(|source| format!("cannot start index batch: {source}"))?;
    {
        let mut statement = transaction
            .prepare_cached(
                "INSERT OR REPLACE INTO files_v2
                 (workspace_id, generation_id, path, relative_path, file_name, extension,
                  size, mtime, indexed_at)
                 VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8,
                         strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))",
            )
            .map_err(|source| format!("cannot prepare index batch: {source}"))?;
        for file in batch.iter() {
            statement
                .execute(params![
                    workspace_id,
                    generation,
                    file.absolute_path,
                    file.relative_path,
                    file.file_name,
                    file.extension,
                    file.size,
                    file.mtime
                ])
                .map_err(|source| format!("cannot write index batch: {source}"))?;
        }
    }
    transaction
        .commit()
        .map_err(|source| format!("cannot commit index batch: {source}"))?;
    batch.clear();
    Ok(())
}

fn prepare_index(
    connection: &mut Connection,
    workspace_id: &str,
    total_directories: u64,
) -> Result<(String, HashSet<String>), String> {
    let resumable = connection
        .query_row(
            "SELECT staging_generation, staging_total_directories
             FROM index_state_v2
             WHERE workspace_id = ?1 AND status = 'indexing' AND staging_generation IS NOT NULL",
            params![workspace_id],
            |row| Ok((row.get::<_, String>(0)?, row.get::<_, i64>(1)?)),
        )
        .ok()
        .filter(|(_, total)| *total == total_directories as i64);
    if let Some((generation, _)) = resumable {
        let mut statement = connection
            .prepare(
                "SELECT relative_path FROM index_directory_progress
                 WHERE workspace_id = ?1 AND generation_id = ?2",
            )
            .map_err(|source| format!("cannot prepare index progress: {source}"))?;
        let paths = statement
            .query_map(params![workspace_id, generation], |row| {
                row.get::<_, String>(0)
            })
            .map_err(|source| format!("cannot read index progress: {source}"))?
            .collect::<Result<HashSet<_>, _>>()
            .map_err(|source| format!("cannot read index progress: {source}"))?;
        return Ok((generation, paths));
    }

    cleanup_staging(connection, workspace_id)?;
    let generation = generation_id();
    connection
        .execute(
            "UPDATE index_state_v2
             SET staging_generation = ?2, staging_total_directories = ?3,
                 staging_completed_directories = 0, status = 'indexing'
             WHERE workspace_id = ?1",
            params![workspace_id, generation, total_directories as i64],
        )
        .map_err(|source| format!("cannot initialize index progress: {source}"))?;
    Ok((generation, HashSet::new()))
}

fn checkpoint_directory(
    connection: &mut Connection,
    workspace_id: &str,
    generation: &str,
    relative_path: &str,
    batch: &mut Vec<IndexedFile>,
) -> Result<(), String> {
    flush_batch(connection, workspace_id, generation, batch)?;
    let transaction = connection
        .transaction()
        .map_err(|source| format!("cannot start directory checkpoint: {source}"))?;
    transaction
        .execute(
            "INSERT OR IGNORE INTO index_directory_progress
             (workspace_id, generation_id, relative_path, completed_at)
             VALUES (?1, ?2, ?3, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))",
            params![workspace_id, generation, relative_path],
        )
        .and_then(|_| {
            transaction.execute(
                "UPDATE index_state_v2 SET staging_completed_directories =
                    (SELECT count(*) FROM index_directory_progress
                     WHERE workspace_id = ?1 AND generation_id = ?2)
                 WHERE workspace_id = ?1",
                params![workspace_id, generation],
            )
        })
        .map_err(|source| format!("cannot save directory checkpoint: {source}"))?;
    transaction
        .commit()
        .map_err(|source| format!("cannot commit directory checkpoint: {source}"))
}

fn publish_generation(
    connection: &mut Connection,
    workspace_id: &str,
    generation: &str,
) -> Result<(), String> {
    let transaction = connection
        .transaction()
        .map_err(|source| format!("cannot start index publication: {source}"))?;
    transaction
        .execute(
            "UPDATE index_state_v2 SET active_generation = ?2, status = 'ready',
                 index_version = index_version + 1,
                 last_scan = strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                 last_full_index = strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                 staging_generation = NULL, staging_total_directories = NULL,
                 staging_completed_directories = 0
             WHERE workspace_id = ?1",
            params![workspace_id, generation],
        )
        .and_then(|_| {
            transaction.execute(
                "DELETE FROM index_directory_progress WHERE workspace_id = ?1",
                params![workspace_id],
            )
        })
        .and_then(|_| {
            transaction.execute(
                "DELETE FROM files_v2 WHERE workspace_id = ?1 AND generation_id <> ?2",
                params![workspace_id, generation],
            )
        })
        .and_then(|_| {
            transaction.execute(
                "UPDATE workspaces SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                     last_indexed_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now') WHERE id = ?1",
                params![workspace_id],
            )
        })
        .map_err(|source| format!("cannot publish index: {source}"))?;
    transaction
        .commit()
        .map_err(|source| format!("cannot commit index publication: {source}"))
}

fn cleanup_staging(connection: &mut Connection, workspace_id: &str) -> Result<(), String> {
    let transaction = connection
        .transaction()
        .map_err(|source| format!("cannot start incomplete index cleanup: {source}"))?;
    transaction
        .execute(
            "DELETE FROM files_v2
             WHERE workspace_id = ?1
               AND generation_id <> COALESCE(
                   (SELECT active_generation FROM index_state_v2 WHERE workspace_id = ?1), '')",
            params![workspace_id],
        )
        .and_then(|_| {
            transaction.execute(
                "DELETE FROM index_directory_progress WHERE workspace_id = ?1",
                params![workspace_id],
            )
        })
        .map_err(|source| format!("cannot remove incomplete index data: {source}"))?;
    transaction
        .commit()
        .map_err(|source| format!("cannot commit incomplete index cleanup: {source}"))
}

fn read_index_status(connection: &Connection, workspace_id: &str) -> Result<IndexStatus, String> {
    connection
        .query_row(
            "SELECT state.workspace_id, state.status,
                    (SELECT count(*) FROM files_v2 AS files
                     WHERE files.workspace_id = state.workspace_id
                       AND files.generation_id = state.active_generation),
                    state.index_version, state.last_scan, state.last_full_index
             FROM index_state_v2 AS state WHERE state.workspace_id = ?1",
            params![workspace_id],
            |row| {
                Ok(IndexStatus {
                    workspace_id: row.get(0)?,
                    status: row.get(1)?,
                    indexed_files: row.get(2)?,
                    index_version: row.get(3)?,
                    last_scan: row.get(4)?,
                    last_full_index: row.get(5)?,
                })
            },
        )
        .map_err(|source| format!("cannot read index status: {source}"))
}

pub fn sqlite_diagnostics() -> Result<SqliteDiagnostics, String> {
    let connection = Connection::open_in_memory()
        .map_err(|source| format!("cannot open SQLite diagnostics database: {source}"))?;
    let version = connection
        .query_row("SELECT sqlite_version()", [], |row| row.get(0))
        .map_err(|source| format!("cannot read SQLite version: {source}"))?;
    let quick_check = connection
        .query_row("PRAGMA quick_check", [], |row| row.get(0))
        .map_err(|source| format!("SQLite quick_check failed: {source}"))?;
    Ok(SqliteDiagnostics {
        version,
        quick_check,
    })
}

fn configure(connection: &Connection) -> Result<(), String> {
    connection
        .pragma_update(None, "journal_mode", "WAL")
        .and_then(|()| connection.pragma_update(None, "synchronous", "NORMAL"))
        .and_then(|()| connection.pragma_update(None, "foreign_keys", "ON"))
        .and_then(|()| connection.pragma_update(None, "temp_store", "FILE"))
        .and_then(|()| connection.pragma_update(None, "cache_size", -65_536_i64))
        .and_then(|()| connection.pragma_update(None, "mmap_size", 0_i64))
        .map_err(|source| format!("cannot configure workspace database: {source}"))
}

fn migrate(connection: &mut Connection) -> Result<(), String> {
    let current_version: i64 = connection
        .pragma_query_value(None, "user_version", |row| row.get(0))
        .map_err(|source| format!("cannot read schema version: {source}"))?;
    if current_version > SCHEMA_VERSION {
        return Err(format!(
            "workspace database schema {current_version} is newer than supported {SCHEMA_VERSION}"
        ));
    }
    let transaction = connection
        .transaction()
        .map_err(|source| format!("cannot start schema migration: {source}"))?;
    transaction
        .execute_batch(
            "CREATE TABLE IF NOT EXISTS workspaces (
                id TEXT PRIMARY KEY,
                path TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_indexed_at TEXT
            );
            CREATE TABLE IF NOT EXISTS index_state_v2 (
                workspace_id TEXT PRIMARY KEY REFERENCES workspaces(id) ON DELETE CASCADE,
                active_generation TEXT,
                index_version INTEGER NOT NULL,
                last_scan TEXT,
                last_full_index TEXT,
                status TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS index_directory_progress (
                workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                generation_id TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                completed_at TEXT NOT NULL,
                PRIMARY KEY(workspace_id, generation_id, relative_path)
            );
            CREATE TABLE IF NOT EXISTS files_v2 (
                workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
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
            PRAGMA user_version = 4;",
        )
        .map_err(|source| format!("schema migration failed: {source}"))?;
    add_column_if_missing(&transaction, "staging_generation", "TEXT")?;
    add_column_if_missing(&transaction, "staging_total_directories", "INTEGER")?;
    add_column_if_missing(
        &transaction,
        "staging_completed_directories",
        "INTEGER NOT NULL DEFAULT 0",
    )?;
    transaction
        .commit()
        .map_err(|source| format!("cannot commit schema migration: {source}"))
}

fn add_column_if_missing(
    connection: &Connection,
    column: &str,
    definition: &str,
) -> Result<(), String> {
    let exists: i64 = connection
        .query_row(
            "SELECT count(*) FROM pragma_table_info('index_state_v2') WHERE name = ?1",
            params![column],
            |row| row.get(0),
        )
        .map_err(|source| format!("cannot inspect index state schema: {source}"))?;
    if exists == 0 {
        connection
            .execute_batch(&format!(
                "ALTER TABLE index_state_v2 ADD COLUMN {column} {definition};"
            ))
            .map_err(|source| format!("cannot add index state column {column}: {source}"))?;
    }
    Ok(())
}

fn register_workspace(
    connection: &Connection,
    id: &str,
    path: &str,
    name: &str,
) -> Result<(), String> {
    connection
        .execute(
            "INSERT INTO workspaces (id, path, name, created_at, updated_at)
             VALUES (?1, ?2, ?3, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                     strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
             ON CONFLICT(id) DO UPDATE SET path = excluded.path, name = excluded.name,
                 updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')",
            params![id, path, name],
        )
        .and_then(|_| {
            connection.execute(
                "INSERT INTO index_state_v2 (workspace_id, index_version, status)
                 VALUES (?1, 0, 'not_indexed') ON CONFLICT(workspace_id) DO NOTHING",
                params![id],
            )
        })
        .map(|_| ())
        .map_err(|source| format!("cannot register workspace: {source}"))
}

fn escape_like(value: &str) -> String {
    value
        .replace('\\', "\\\\")
        .replace('%', "\\%")
        .replace('_', "\\_")
}

fn generation_id() -> String {
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_or(0, |value| value.as_nanos());
    format!("{}-{nanos}", std::process::id())
}

fn platform_data_root() -> Result<PathBuf, String> {
    if let Some(path) = std::env::var_os("SHIORI_DATA_HOME") {
        return Ok(PathBuf::from(path));
    }
    if cfg!(target_os = "windows") {
        return std::env::var_os("LOCALAPPDATA")
            .map(PathBuf::from)
            .map(|path| path.join("Shiori"))
            .ok_or_else(|| "LOCALAPPDATA is unavailable".to_owned());
    }
    if cfg!(target_os = "macos") {
        return std::env::var_os("HOME")
            .map(PathBuf::from)
            .map(|path| {
                path.join("Library")
                    .join("Application Support")
                    .join("Shiori")
            })
            .ok_or_else(|| "HOME is unavailable".to_owned());
    }
    std::env::var_os("XDG_DATA_HOME")
        .map(PathBuf::from)
        .or_else(|| {
            std::env::var_os("HOME").map(|home| PathBuf::from(home).join(".local").join("share"))
        })
        .map(|path| path.join("shiori"))
        .ok_or_else(|| "data directory is unavailable".to_owned())
}

fn normalize_path(root: &Path) -> String {
    let mut value = root.to_string_lossy().replace('\\', "/");
    if cfg!(target_os = "windows") {
        if let Some(path) = value.strip_prefix("//?/") {
            value = path.to_owned();
        }
        value.to_lowercase()
    } else {
        value
    }
}

fn workspace_id(normalized_path: &str) -> String {
    Sha256::digest(normalized_path.as_bytes()).iter().fold(
        String::with_capacity(64),
        |mut output, byte| {
            write!(output, "{byte:02x}").expect("writing to String cannot fail");
            output
        },
    )
}

#[cfg(test)]
mod tests {
    use super::WorkspaceDatabase;
    use std::fs;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn build_index_streams_metadata_and_publishes_results() {
        let test_root = temporary_root();
        let workspace = test_root.join("workspace");
        let data = test_root.join("data");
        fs::create_dir_all(workspace.join("src")).expect("workspace should be created");
        fs::write(workspace.join("src").join("main.rs"), "fn main() {}")
            .expect("source file should be written");
        let database = WorkspaceDatabase::open_at(&workspace, &data).expect("database should open");
        let mut progress = Vec::new();

        let status = database
            .build_index(&workspace, 2, |completed, total, path| {
                progress.push((completed, total, path.to_owned()));
            })
            .expect("index should build");
        let results = database
            .search_files("main", 20)
            .expect("index should search");

        assert_eq!(status.status, "ready");
        assert_eq!(status.indexed_files, 1);
        assert_eq!(progress.len(), 2);
        assert_eq!(results, [std::path::PathBuf::from("src/main.rs")]);
        drop(database);
        fs::remove_dir_all(test_root).expect("test directory should be removed");
    }

    #[test]
    fn build_index_more_than_one_batch_publishes_every_file() {
        let test_root = temporary_root();
        let workspace = test_root.join("workspace");
        let data = test_root.join("data");
        fs::create_dir_all(&workspace).expect("workspace should be created");
        for index in 0..1_005 {
            fs::write(workspace.join(format!("file-{index:04}.txt")), [])
                .expect("test file should be written");
        }
        let database = WorkspaceDatabase::open_at(&workspace, &data).expect("database should open");

        let status = database
            .build_index(&workspace, 1, |_, _, _| {})
            .expect("multi-batch index should build");

        assert_eq!(status.indexed_files, 1_005);
        drop(database);
        fs::remove_dir_all(test_root).expect("test directory should be removed");
    }

    #[test]
    fn build_index_resumes_after_completed_directory() {
        let test_root = temporary_root();
        let workspace = test_root.join("workspace");
        let data = test_root.join("data");
        fs::create_dir_all(workspace.join("completed")).expect("completed directory should exist");
        fs::create_dir_all(workspace.join("remaining")).expect("remaining directory should exist");
        fs::write(workspace.join("completed").join("old.txt"), "old")
            .expect("completed file should be written");
        fs::write(workspace.join("remaining").join("new.txt"), "new")
            .expect("remaining file should be written");
        let database = WorkspaceDatabase::open_at(&workspace, &data).expect("database should open");
        {
            let connection = database.connection.lock().expect("database should lock");
            connection
                .execute(
                    "UPDATE index_state_v2 SET status = 'indexing', staging_generation = 'staging',
                         staging_total_directories = 3, staging_completed_directories = 1
                     WHERE workspace_id = ?1",
                    rusqlite::params![database.info.id],
                )
                .expect("staging state should be written");
            connection
                .execute(
                    "INSERT INTO index_directory_progress
                     (workspace_id, generation_id, relative_path, completed_at)
                     VALUES (?1, 'staging', 'completed', 'now')",
                    rusqlite::params![database.info.id],
                )
                .expect("directory checkpoint should be written");
            connection
                .execute(
                    "INSERT INTO files_v2
                     (workspace_id, generation_id, path, relative_path, file_name, extension,
                      size, mtime, indexed_at)
                     VALUES (?1, 'staging', ?2, 'completed/old.txt', 'old.txt', 'txt', 3, 0, 'now')",
                    rusqlite::params![
                        database.info.id,
                        workspace.join("completed").join("old.txt").to_string_lossy()
                    ],
                )
                .expect("staged file should be written");
        }
        let mut progress = Vec::new();

        let status = database
            .build_index(&workspace, 3, |completed, _, path| {
                progress.push((completed, path.to_owned()));
            })
            .expect("index should resume");
        let results = database
            .search_files(".txt", 20)
            .expect("index should search");

        assert_eq!(status.indexed_files, 2);
        assert_eq!(progress, [(2, "remaining".to_owned()), (3, ".".to_owned())]);
        assert_eq!(results.len(), 2);
        {
            let connection = database.connection.lock().expect("database should lock");
            let checkpoints: i64 = connection
                .query_row("SELECT count(*) FROM index_directory_progress", [], |row| {
                    row.get(0)
                })
                .expect("checkpoint count should be readable");
            let staging_generation: Option<String> = connection
                .query_row(
                    "SELECT staging_generation FROM index_state_v2 WHERE workspace_id = ?1",
                    rusqlite::params![database.info.id],
                    |row| row.get(0),
                )
                .expect("staging state should be readable");
            assert_eq!(checkpoints, 0);
            assert_eq!(staging_generation, None);
        }
        drop(database);
        fs::remove_dir_all(test_root).expect("test directory should be removed");
    }

    #[test]
    fn unified_database_isolates_workspace_indexes() {
        let test_root = temporary_root();
        let first_workspace = test_root.join("first");
        let second_workspace = test_root.join("second");
        let data = test_root.join("data");
        fs::create_dir_all(&first_workspace).expect("first workspace should be created");
        fs::create_dir_all(&second_workspace).expect("second workspace should be created");
        fs::write(first_workspace.join("first.txt"), []).expect("first file should be written");
        fs::write(second_workspace.join("second.txt"), []).expect("second file should be written");
        let first = WorkspaceDatabase::open_at(&first_workspace, &data)
            .expect("first database should open");
        let second = WorkspaceDatabase::open_at(&second_workspace, &data)
            .expect("second database should open");

        first
            .build_index(&first_workspace, 1, |_, _, _| {})
            .expect("first index should build");
        second
            .build_index(&second_workspace, 1, |_, _, _| {})
            .expect("second index should build");

        assert_eq!(first.info().database_path, second.info().database_path);
        assert_eq!(
            first
                .search_files("first", 20)
                .expect("first search should work")
                .len(),
            1
        );
        assert!(
            first
                .search_files("second", 20)
                .expect("isolated search should work")
                .is_empty()
        );
        assert_eq!(
            second
                .search_files("second", 20)
                .expect("second search should work")
                .len(),
            1
        );
        drop(first);
        drop(second);
        fs::remove_dir_all(test_root).expect("test directory should be removed");
    }

    fn temporary_root() -> std::path::PathBuf {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system time should be valid")
            .as_nanos();
        std::env::temp_dir().join(format!("shiori-db-test-{}-{unique}", std::process::id()))
    }
}
