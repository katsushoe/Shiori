use rusqlite::{Connection, params};
use sha2::{Digest, Sha256};
use std::fmt::Write;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

const SCHEMA_VERSION: i64 = 1;

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

impl WorkspaceDatabase {
    pub fn open(root: &Path) -> Result<Self, String> {
        let data_root = platform_data_root()?;
        Self::open_at(root, &data_root)
    }

    fn open_at(root: &Path, data_root: &Path) -> Result<Self, String> {
        let normalized_path = normalize_path(root);
        let workspace_id = workspace_id(&normalized_path);
        let database_directory = data_root.join("indexes").join(&workspace_id);
        fs::create_dir_all(&database_directory)
            .map_err(|source| format!("cannot create index directory: {source}"))?;
        let database_path = database_directory.join("shiori.db");
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
        let connection = self
            .connection
            .lock()
            .map_err(|_| "workspace database lock is poisoned".to_owned())?;
        let result: String = connection
            .query_row("PRAGMA quick_check", [], |row| row.get(0))
            .map_err(|source| format!("workspace database validation failed: {source}"))?;
        if result != "ok" {
            return Err(format!("workspace database validation failed: {result}"));
        }
        Ok(())
    }
}

fn configure(connection: &Connection) -> Result<(), String> {
    connection
        .pragma_update(None, "journal_mode", "WAL")
        .and_then(|()| connection.pragma_update(None, "synchronous", "NORMAL"))
        .and_then(|()| connection.pragma_update(None, "foreign_keys", "ON"))
        .and_then(|()| connection.pragma_update(None, "temp_store", "MEMORY"))
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
    if current_version == SCHEMA_VERSION {
        return Ok(());
    }

    let transaction = connection
        .transaction()
        .map_err(|source| format!("cannot start schema migration: {source}"))?;
    transaction
        .execute_batch(
            "CREATE TABLE workspaces (
                id TEXT PRIMARY KEY,
                path TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_indexed_at TEXT
            );
            CREATE TABLE files (
                id INTEGER PRIMARY KEY,
                workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                path TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                extension TEXT,
                language TEXT,
                size INTEGER NOT NULL,
                mtime INTEGER NOT NULL,
                content_hash TEXT,
                indexed_at TEXT,
                UNIQUE(workspace_id, relative_path)
            );
            CREATE INDEX files_workspace_relative_path ON files(workspace_id, relative_path);
            CREATE INDEX files_workspace_extension ON files(workspace_id, extension);
            CREATE INDEX files_workspace_language ON files(workspace_id, language);
            CREATE INDEX files_workspace_mtime ON files(workspace_id, mtime);
            CREATE TABLE symbols (
                id INTEGER PRIMARY KEY,
                workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                qualified_name TEXT,
                kind TEXT NOT NULL,
                language TEXT NOT NULL,
                start_line INTEGER NOT NULL,
                start_column INTEGER NOT NULL,
                end_line INTEGER NOT NULL,
                end_column INTEGER NOT NULL,
                parent_symbol_id INTEGER REFERENCES symbols(id) ON DELETE SET NULL,
                signature TEXT
            );
            CREATE VIRTUAL TABLE symbols_fts USING fts5(name, qualified_name, signature, content='symbols', content_rowid='id');
            CREATE TABLE symbol_references (
                id INTEGER PRIMARY KEY,
                workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                symbol_id INTEGER REFERENCES symbols(id) ON DELETE SET NULL,
                target_name TEXT NOT NULL,
                reference_kind TEXT,
                line INTEGER NOT NULL,
                column_number INTEGER NOT NULL
            );
            CREATE TABLE dependencies (
                id INTEGER PRIMARY KEY,
                workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
                source_file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                target TEXT NOT NULL,
                dependency_type TEXT NOT NULL,
                line INTEGER NOT NULL
            );
            CREATE TABLE index_state (
                workspace_id TEXT PRIMARY KEY REFERENCES workspaces(id) ON DELETE CASCADE,
                index_version INTEGER NOT NULL,
                parser_version TEXT,
                last_scan TEXT,
                last_full_index TEXT,
                status TEXT NOT NULL
            );
            PRAGMA user_version = 1;",
        )
        .map_err(|source| format!("schema migration failed: {source}"))?;
    transaction
        .commit()
        .map_err(|source| format!("cannot commit schema migration: {source}"))
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
             VALUES (?1, ?2, ?3, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
             ON CONFLICT(id) DO UPDATE SET path = excluded.path, name = excluded.name,
                 updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')",
            params![id, path, name],
        )
        .and_then(|_| {
            connection.execute(
                "INSERT INTO index_state (workspace_id, index_version, status)
                 VALUES (?1, 1, 'not_indexed') ON CONFLICT(workspace_id) DO NOTHING",
                params![id],
            )
        })
        .map(|_| ())
        .map_err(|source| format!("cannot register workspace: {source}"))
}

fn platform_data_root() -> Result<PathBuf, String> {
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
    fn open_at_creates_schema_and_registers_workspace() {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system time should be valid")
            .as_nanos();
        let test_root =
            std::env::temp_dir().join(format!("shiori-db-test-{}-{unique}", std::process::id()));
        let workspace = test_root.join("workspace");
        let data = test_root.join("data");
        fs::create_dir_all(&workspace).expect("workspace should be created");

        let database = WorkspaceDatabase::open_at(&workspace, &data).expect("database should open");

        assert_eq!(database.info().schema_version, 1);
        assert!(database.info().database_path.ends_with("shiori.db"));
        database.validate().expect("database should be valid");
        let connection = database.connection.lock().expect("database should lock");
        let journal_mode: String = connection
            .pragma_query_value(None, "journal_mode", |row| row.get(0))
            .expect("journal mode should be readable");
        let foreign_keys: i64 = connection
            .pragma_query_value(None, "foreign_keys", |row| row.get(0))
            .expect("foreign key setting should be readable");
        let table_count: i64 = connection
            .query_row(
                "SELECT count(*) FROM sqlite_master WHERE type IN ('table', 'view')",
                [],
                |row| row.get(0),
            )
            .expect("schema should be readable");
        assert_eq!(journal_mode, "wal");
        assert_eq!(foreign_keys, 1);
        assert!(table_count >= 8);
        drop(connection);
        drop(database);
        fs::remove_dir_all(test_root).expect("test directory should be removed");
    }
}
