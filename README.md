# Shiori

Shiori is a local-first code search and navigation server for AI coding agents.

The C# host owns MCP, CLI, configuration, and query planning. A Rust Native DLL
owns performance-sensitive search, indexing, SQLite, and Tree-sitter operations
behind a versioned C ABI.

Product version: `0.0.0.0`.

## Commands

```text
dotnet run --project src/Shiori.Cli -- find <query> --allow <directory> [--limit <count>]
dotnet run --project src/Shiori.Cli -- grep <query> --allow <directory> [--glob <glob>] [--regex]
dotnet run --project src/Shiori.Cli -- index build --allow <directory>
dotnet run --project src/Shiori.Cli -- index status --allow <directory>
dotnet run --project src/Shiori.Cli -- index rebuild --allow <directory>
dotnet run --project src/Shiori.Cli -- outline <source-file> --allow <directory>
dotnet run --project src/Shiori.Cli -- symbol <query> --allow <directory> [--kind <kind>] [--language <language>]
dotnet run --project src/Shiori.Cli -- workspace add <absolute-directory>
dotnet run --project src/Shiori.Cli -- workspace list
dotnet run --project src/Shiori.Cli -- workspace remove <name-or-id-or-absolute-directory>
dotnet run --project src/Shiori.Cli -- doctor
dotnet run --project src/Shiori.Cli -- serve --port 39473
```

The server exposes one stateless Streamable HTTP endpoint at
`http://127.0.0.1:39473/mcp`. Set `SHIORI_MCP_TOKEN` to a random value of at
least 32 characters and `SHIORI_ALLOWED_WORKSPACES` to a semicolon-separated
list of absolute workspace paths before starting it.

Opening a configured workspace creates its SQLite database under the platform
data directory (`%LOCALAPPDATA%\Shiori\indexes\<workspace-id>\shiori.db` on
Windows). File indexing honors `.gitignore` plus Shiori's default build and
dependency-directory exclusions. File-name searches lazily build and then use
this persistent index.

The MCP server exposes `workspace_list`, `index_status`, `reindex`,
`search_files`, `search_text`, `search_symbols`, and `file_outline`. `reindex` builds a missing
index by default; set `force` to `true` to run a full rescan.

CLI workspace registrations are stored in the current user's local application
data directory. Removing a registration preserves its SQLite index database.
Registrations do not grant MCP access; `SHIORI_ALLOWED_WORKSPACES` remains the
server authorization boundary.

Set `SHIORI_DATA_HOME` to override the shared workspace-registry and index-data
directory, for example when running Shiori in an isolated environment.

`doctor` reports Native DLL/ABI, SQLite quick-check and FTS5 support, ripgrep,
Tree-sitter grammar availability, data-directory write access, and MCP
environment configuration as structured JSON. Missing optional MCP settings
produce warnings; required runtime failures return exit code 1.

Tree-sitter language detection supports C#, TypeScript/TSX, JavaScript, Python,
Rust, Go, Java, C, and C++ in the v1 parser set.
Full index builds extract namespaces/modules, types, functions, methods,
constructors, properties, fields, and constants into SQLite `symbols` while
maintaining parent and qualified-name relationships.

Set `SHIORI_EXCLUDE_PATTERNS` to semicolon-separated gitignore-style glob
patterns (for example, `generated/**;*.min.js`) to add workspace exclusions.

Build `native/shiori-engine` as a `cdylib` and place the resulting
`shiori_engine` native library beside the managed executable. Search operations
never read outside the canonical `--allow` workspace.
