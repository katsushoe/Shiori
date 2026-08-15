use crate::index::IndexedFile;
use rusqlite::{Connection, OptionalExtension, params};
use serde::Serialize;
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

#[derive(Serialize)]
pub struct IndexStatus {
    pub workspace_id: String,
    pub status: String,
    pub indexed_files: i64,
    pub indexed_symbols: i64,
    pub index_version: i64,
    pub parser_version: Option<String>,
    pub last_scan: Option<String>,
    pub last_full_index: Option<String>,
}

#[derive(Serialize)]
pub struct SqliteDiagnostics {
    pub version: String,
    pub quick_check: String,
    pub fts5_enabled: bool,
}

#[derive(Serialize)]
pub struct FileOutline {
    pub path: String,
    pub language: Option<String>,
    pub symbols: Vec<OutlineSymbol>,
}

#[derive(Serialize)]
pub struct OutlineSymbol {
    pub name: String,
    pub qualified_name: String,
    pub kind: String,
    pub language: String,
    pub start_line: i64,
    pub start_column: i64,
    pub end_line: i64,
    pub end_column: i64,
    pub signature: Option<String>,
    pub children: Vec<OutlineSymbol>,
}

#[derive(Serialize)]
pub struct SymbolSearchResponse {
    pub results: Vec<SymbolSearchResult>,
}

#[derive(Serialize)]
pub struct SymbolSearchResult {
    pub r#type: &'static str,
    pub name: String,
    pub qualified_name: Option<String>,
    pub kind: String,
    pub language: String,
    pub path: String,
    pub line: i64,
    pub column: i64,
    pub score: f64,
    pub signature: Option<String>,
}

struct StoredSymbol {
    id: i64,
    parent_id: Option<i64>,
    name: String,
    qualified_name: String,
    kind: String,
    language: String,
    start_line: i64,
    start_column: i64,
    end_line: i64,
    end_column: i64,
    signature: Option<String>,
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

    pub fn replace_file_index(&self, files: &[IndexedFile]) -> Result<(), String> {
        let mut connection = self
            .connection
            .lock()
            .map_err(|_| "workspace database lock is poisoned".to_owned())?;
        let transaction = connection
            .transaction()
            .map_err(|source| format!("cannot start file index update: {source}"))?;
        transaction
            .execute_batch("CREATE TEMP TABLE indexed_paths (relative_path TEXT PRIMARY KEY);")
            .map_err(|source| format!("cannot initialize file index update: {source}"))?;
        for file in files {
            transaction
                .execute(
                    "INSERT INTO files
                     (workspace_id, path, relative_path, extension, language, size, mtime, indexed_at)
                     VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
                     ON CONFLICT(workspace_id, relative_path) DO UPDATE SET
                         path = excluded.path, extension = excluded.extension,
                         language = excluded.language, size = excluded.size,
                         mtime = excluded.mtime, indexed_at = excluded.indexed_at",
                    params![
                        self.info.id,
                        file.absolute_path,
                        file.relative_path,
                        file.extension,
                        file.language,
                        file.size,
                        file.mtime
                    ],
                )
                .and_then(|_| {
                    transaction.execute(
                        "INSERT INTO indexed_paths (relative_path) VALUES (?1)",
                        params![file.relative_path],
                    )
                })
                .map_err(|source| format!("cannot update indexed file: {source}"))?;
            let file_id: i64 = transaction
                .query_row(
                    "SELECT id FROM files WHERE workspace_id = ?1 AND relative_path = ?2",
                    params![self.info.id, file.relative_path],
                    |row| row.get(0),
                )
                .map_err(|source| format!("cannot resolve indexed file: {source}"))?;
            transaction
                .execute("DELETE FROM symbols WHERE file_id = ?1", params![file_id])
                .map_err(|source| format!("cannot replace file symbols: {source}"))?;
            let mut symbol_ids = Vec::with_capacity(file.symbols.len());
            for symbol in &file.symbols {
                let parent_id = symbol
                    .parent_index
                    .and_then(|index| symbol_ids.get(index))
                    .copied();
                transaction
                    .execute(
                        "INSERT INTO symbols
                         (workspace_id, file_id, name, qualified_name, kind, language,
                          start_line, start_column, end_line, end_column, parent_symbol_id, signature)
                         VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12)",
                        params![
                            self.info.id,
                            file_id,
                            symbol.name,
                            symbol.qualified_name,
                            symbol.kind,
                            symbol.language,
                            symbol.start_line,
                            symbol.start_column,
                            symbol.end_line,
                            symbol.end_column,
                            parent_id,
                            symbol.signature
                        ],
                    )
                    .map_err(|source| format!("cannot store file symbol: {source}"))?;
                symbol_ids.push(transaction.last_insert_rowid());
            }
        }
        transaction
            .execute(
                "DELETE FROM files WHERE workspace_id = ?1
                 AND relative_path NOT IN (SELECT relative_path FROM indexed_paths)",
                params![self.info.id],
            )
            .and_then(|_| {
                transaction.execute(
                    "UPDATE workspaces SET
                         updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                         last_indexed_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                     WHERE id = ?1",
                    params![self.info.id],
                )
            })
            .and_then(|_| {
                transaction.execute(
                    "UPDATE index_state SET
                         last_scan = strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                         last_full_index = strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
                         parser_version = ?2,
                         status = 'ready'
                     WHERE workspace_id = ?1",
                    params![self.info.id, crate::languages::PARSER_VERSION],
                )
            })
            .and_then(|_| {
                transaction.execute("INSERT INTO symbols_fts(symbols_fts) VALUES('rebuild')", [])
            })
            .and_then(|_| transaction.execute("DROP TABLE indexed_paths", []))
            .map_err(|source| format!("cannot finalize file index update: {source}"))?;
        transaction
            .commit()
            .map_err(|source| format!("cannot commit file index update: {source}"))
    }

    pub fn index_status(&self) -> Result<IndexStatus, String> {
        let connection = self
            .connection
            .lock()
            .map_err(|_| "workspace database lock is poisoned".to_owned())?;
        connection
            .query_row(
                "SELECT state.workspace_id, state.status,
                        (SELECT count(*) FROM files WHERE workspace_id = state.workspace_id),
                        (SELECT count(*) FROM symbols WHERE workspace_id = state.workspace_id),
                        state.index_version, state.parser_version,
                        state.last_scan, state.last_full_index
                 FROM index_state AS state
                 WHERE state.workspace_id = ?1",
                params![self.info.id],
                |row| {
                    Ok(IndexStatus {
                        workspace_id: row.get(0)?,
                        status: row.get(1)?,
                        indexed_files: row.get(2)?,
                        indexed_symbols: row.get(3)?,
                        index_version: row.get(4)?,
                        parser_version: row.get(5)?,
                        last_scan: row.get(6)?,
                        last_full_index: row.get(7)?,
                    })
                },
            )
            .map_err(|source| format!("cannot read index status: {source}"))
    }

    pub fn search_files(&self, query: &str, limit: usize) -> Result<Vec<PathBuf>, String> {
        let connection = self
            .connection
            .lock()
            .map_err(|_| "workspace database lock is poisoned".to_owned())?;
        let pattern = format!("%{}%", escape_like(query));
        let mut statement = connection
            .prepare(
                "SELECT relative_path FROM files
                 WHERE workspace_id = ?1 AND relative_path LIKE ?2 ESCAPE '\\'
                 ORDER BY relative_path COLLATE NOCASE LIMIT ?3",
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

    pub fn search_symbols(
        &self,
        query: &str,
        kind: Option<&str>,
        language: Option<&str>,
        path: Option<&str>,
        limit: usize,
    ) -> Result<SymbolSearchResponse, String> {
        let connection = self
            .connection
            .lock()
            .map_err(|_| "workspace database lock is poisoned".to_owned())?;
        let fts_query = format!("\"{}\"*", query.replace('"', "\"\""));
        let path_pattern = path.map(|value| format!("%{}%", escape_like(value)));
        let mut statement = connection
            .prepare(
                "SELECT symbols.name, symbols.qualified_name, symbols.kind, symbols.language,
                        files.relative_path, symbols.start_line, symbols.start_column,
                        CASE
                            WHEN symbols.name = ?2 COLLATE NOCASE THEN 1.0
                            WHEN symbols.name LIKE (?2 || '%') ESCAPE '\\' COLLATE NOCASE THEN 0.9
                            ELSE 0.7
                        END AS score,
                        symbols.signature
                 FROM symbols_fts
                 JOIN symbols ON symbols.id = symbols_fts.rowid
                 JOIN files ON files.id = symbols.file_id
                 WHERE symbols_fts MATCH ?1
                   AND symbols.workspace_id = ?3
                   AND (?4 IS NULL OR symbols.kind = ?4 COLLATE NOCASE)
                   AND (?5 IS NULL OR symbols.language = ?5 COLLATE NOCASE)
                   AND (?6 IS NULL OR files.relative_path LIKE ?6 ESCAPE '\\' COLLATE NOCASE)
                 ORDER BY score DESC, bm25(symbols_fts), symbols.name COLLATE NOCASE,
                          files.relative_path COLLATE NOCASE, symbols.start_line
                 LIMIT ?7",
            )
            .map_err(|source| format!("cannot prepare symbol search: {source}"))?;
        let rows = statement
            .query_map(
                params![
                    fts_query,
                    query,
                    self.info.id,
                    kind,
                    language,
                    path_pattern,
                    limit as i64
                ],
                |row| {
                    Ok(SymbolSearchResult {
                        r#type: "symbol",
                        name: row.get(0)?,
                        qualified_name: row.get(1)?,
                        kind: row.get(2)?,
                        language: row.get(3)?,
                        path: row.get(4)?,
                        line: row.get(5)?,
                        column: row.get(6)?,
                        score: row.get(7)?,
                        signature: row.get(8)?,
                    })
                },
            )
            .map_err(|source| format!("cannot search symbols: {source}"))?;
        let results = rows
            .map(|row| row.map_err(|source| format!("cannot read symbol search result: {source}")))
            .collect::<Result<Vec<_>, _>>()?;
        Ok(SymbolSearchResponse { results })
    }

    pub fn file_outline(&self, relative_path: &str) -> Result<FileOutline, String> {
        let connection = self
            .connection
            .lock()
            .map_err(|_| "workspace database lock is poisoned".to_owned())?;
        let file = connection
            .query_row(
                "SELECT id, relative_path, language FROM files
                 WHERE workspace_id = ?1 AND relative_path = ?2",
                params![self.info.id, relative_path],
                |row| {
                    Ok((
                        row.get::<_, i64>(0)?,
                        row.get::<_, String>(1)?,
                        row.get::<_, Option<String>>(2)?,
                    ))
                },
            )
            .optional()
            .map_err(|source| format!("cannot resolve outline file: {source}"))?
            .ok_or_else(|| format!("file is not indexed: {relative_path}"))?;
        let mut statement = connection
            .prepare(
                "SELECT id, parent_symbol_id, name, qualified_name, kind, language,
                        start_line, start_column, end_line, end_column, signature
                 FROM symbols WHERE workspace_id = ?1 AND file_id = ?2
                 ORDER BY start_line, start_column, id",
            )
            .map_err(|source| format!("cannot prepare file outline: {source}"))?;
        let rows = statement
            .query_map(params![self.info.id, file.0], |row| {
                Ok(StoredSymbol {
                    id: row.get(0)?,
                    parent_id: row.get(1)?,
                    name: row.get(2)?,
                    qualified_name: row.get(3)?,
                    kind: row.get(4)?,
                    language: row.get(5)?,
                    start_line: row.get(6)?,
                    start_column: row.get(7)?,
                    end_line: row.get(8)?,
                    end_column: row.get(9)?,
                    signature: row.get(10)?,
                })
            })
            .map_err(|source| format!("cannot read file outline: {source}"))?;
        let stored = rows
            .map(|row| row.map_err(|source| format!("cannot read outline symbol: {source}")))
            .collect::<Result<Vec<_>, _>>()?;
        Ok(FileOutline {
            path: file.1,
            language: file.2,
            symbols: outline_children(&stored, None),
        })
    }

    #[cfg(test)]
    fn file_count(&self) -> i64 {
        let connection = self.connection.lock().expect("database should lock");
        connection
            .query_row("SELECT count(*) FROM files", [], |row| row.get(0))
            .expect("file count should be readable")
    }

    #[cfg(test)]
    fn symbol_count(&self) -> i64 {
        let connection = self.connection.lock().expect("database should lock");
        connection
            .query_row("SELECT count(*) FROM symbols", [], |row| row.get(0))
            .expect("symbol count should be readable")
    }
}

fn outline_children(symbols: &[StoredSymbol], parent_id: Option<i64>) -> Vec<OutlineSymbol> {
    symbols
        .iter()
        .filter(|symbol| symbol.parent_id == parent_id)
        .map(|symbol| OutlineSymbol {
            name: symbol.name.clone(),
            qualified_name: symbol.qualified_name.clone(),
            kind: symbol.kind.clone(),
            language: symbol.language.clone(),
            start_line: symbol.start_line,
            start_column: symbol.start_column,
            end_line: symbol.end_line,
            end_column: symbol.end_column,
            signature: symbol.signature.clone(),
            children: outline_children(symbols, Some(symbol.id)),
        })
        .collect()
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
    let fts5_enabled = connection
        .query_row(
            "SELECT sqlite_compileoption_used('ENABLE_FTS5')",
            [],
            |row| row.get::<_, i64>(0),
        )
        .map(|value| value == 1)
        .map_err(|source| format!("cannot read SQLite FTS5 support: {source}"))?;
    Ok(SqliteDiagnostics {
        version,
        quick_check,
        fts5_enabled,
    })
}

fn escape_like(value: &str) -> String {
    value
        .replace('\\', "\\\\")
        .replace('%', "\\%")
        .replace('_', "\\_")
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
    use crate::index;
    use std::fs;
    use std::path::PathBuf;
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

    #[test]
    fn replace_file_index_persists_current_workspace_files() {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system time should be valid")
            .as_nanos();
        let test_root = std::env::temp_dir().join(format!(
            "shiori-db-index-test-{}-{unique}",
            std::process::id()
        ));
        let workspace = test_root.join("workspace");
        let data = test_root.join("data");
        fs::create_dir_all(&workspace).expect("workspace should be created");
        fs::write(workspace.join("sample.rs"), "fn sample() {}").expect("sample should be written");
        let database = WorkspaceDatabase::open_at(&workspace, &data).expect("database should open");

        let initial_status = database.index_status().expect("status should be readable");
        assert_eq!(initial_status.status, "not_indexed");
        assert_eq!(initial_status.indexed_files, 0);
        assert_eq!(initial_status.indexed_symbols, 0);

        database
            .replace_file_index(&index::scan(&workspace).expect("workspace should scan"))
            .expect("file index should be stored");

        assert_eq!(database.file_count(), 1);
        assert_eq!(database.symbol_count(), 1);
        let outline = database
            .file_outline("sample.rs")
            .expect("outline should be readable");
        assert_eq!(outline.language.as_deref(), Some("rust"));
        assert_eq!(outline.symbols.len(), 1);
        assert_eq!(outline.symbols[0].name, "sample");
        assert_eq!(outline.symbols[0].kind, "function");
        let symbols = database
            .search_symbols("sam", None, None, Some("sample"), 10)
            .expect("symbol search should succeed");
        assert_eq!(symbols.results.len(), 1);
        assert_eq!(symbols.results[0].name, "sample");
        assert_eq!(symbols.results[0].language, "rust");
        assert_eq!(symbols.results[0].score, 0.9);
        assert!(
            database
                .search_symbols("sample", Some("method"), None, None, 10)
                .expect("filtered symbol search should succeed")
                .results
                .is_empty()
        );
        let ready_status = database.index_status().expect("status should be readable");
        assert_eq!(ready_status.status, "ready");
        assert_eq!(ready_status.indexed_files, 1);
        assert_eq!(ready_status.indexed_symbols, 1);
        assert_eq!(
            ready_status.parser_version.as_deref(),
            Some(crate::languages::PARSER_VERSION)
        );
        assert!(ready_status.last_full_index.is_some());
        assert_eq!(
            database
                .search_files("sample", 10)
                .expect("search should succeed"),
            vec![PathBuf::from("sample.rs")]
        );
        drop(database);
        fs::remove_dir_all(test_root).expect("test directory should be removed");
    }
}
