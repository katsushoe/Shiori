# Shiori

Shiori is a local-first code search and navigation server for AI coding agents.

The C# host owns MCP, CLI, configuration, and query planning. A Rust Native DLL
owns performance-sensitive search, indexing, SQLite, and Tree-sitter operations
behind a versioned C ABI.

Product version: `1.0.0`.

## Commands

```text
dotnet run --project src/Shiori.Cli -- find <query> --allow <directory> [--limit <count>]
dotnet run --project src/Shiori.Cli -- search <query> --allow <directory> [--path <path>] [--limit <count>]
dotnet run --project src/Shiori.Cli -- grep <query> --allow <directory> [--glob <glob>] [--regex]
dotnet run --project src/Shiori.Cli -- index build --allow <directory>
dotnet run --project src/Shiori.Cli -- index status --allow <directory>
dotnet run --project src/Shiori.Cli -- index rebuild --allow <directory>
dotnet run --project src/Shiori.Cli -- navigate <definition|references> <file> --line <line> --column <column> --allow <directory>
dotnet run --project src/Shiori.Cli -- outline <source-file> --allow <directory>
dotnet run --project src/Shiori.Cli -- symbol <query> --allow <directory> [--kind <kind>] [--language <language>]
dotnet run --project src/Shiori.Cli -- workspace add <absolute-directory>
dotnet run --project src/Shiori.Cli -- workspace list
dotnet run --project src/Shiori.Cli -- workspace remove <name-or-id-or-absolute-directory>
dotnet run --project src/Shiori.Cli -- doctor
dotnet run --project src/Shiori.Cli -- config claude > .mcp.json
dotnet run --project src/Shiori.Cli -- config codex
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
this persistent index. Running `index build` on a ready workspace performs an
incremental scan: unchanged files are retained, content hashes confirm metadata
changes, and only added, changed, or deleted files update SQLite and symbols.
The MCP server watches allowed workspaces recursively and debounces bursts of
create, modify, rename, and delete events into incremental index builds.

The MCP server exposes `search`, `navigate`, `workspace_list`, `index_status`, `reindex`,
`search_files`, `search_text`, `search_symbols`, and `file_outline`. `reindex` builds a missing
index by default; set `force` to `true` to run a full rescan.

The managed query planner classifies file paths, code identifiers, quoted text,
and reference or implementation intent into deterministic file, symbol, and
text-provider plans. The unified `search` tool executes selected providers in
parallel, ranks exact and prefix symbol matches ahead of filenames, paths, and
text matches, deduplicates code locations, and reports recoverable provider errors.

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

For v1.1 semantic navigation, `doctor` discovers `csharp-ls` or `OmniSharp` on
`PATH`. Set `SHIORI_CSHARP_LSP_PATH` to an absolute executable path to select a
specific C# language server. Discovery does not start the server; LSP processes
remain lazy and are started only by semantic-navigation tools.
`navigate` supports the `definition` and `references` actions. Reference results
include declarations and accept `--limit` from 1 to 100. Input lines and columns
are one-based; results use workspace-relative paths and one-based positions.

## Claude Code

Run `shiori config claude > .mcp.json` in the Claude Code project, set
`SHIORI_MCP_TOKEN` to the same bearer token used by the running Shiori server,
then restart Claude Code and inspect `/mcp`. The generated project-scoped config
uses Streamable HTTP at `http://127.0.0.1:39473/mcp` and references the token by
environment variable instead of writing its value to disk.

## Codex

Run `shiori config codex` and merge the generated TOML into
`%USERPROFILE%\.codex\config.toml`. Set `SHIORI_MCP_TOKEN` to the same bearer
token used by the running Shiori server, then start a new Codex task. The
generated config uses Streamable HTTP at `http://127.0.0.1:39473/mcp` and reads
the token from the environment without writing its value to disk.

Set `SHIORI_EXCLUDE_PATTERNS` to semicolon-separated gitignore-style glob
patterns (for example, `generated/**;*.min.js`) to add workspace exclusions.

Build `native/shiori-engine` as a `cdylib` and place the resulting
`shiori_engine` native library beside the managed executable. Search operations
never read outside the canonical `--allow` workspace.

## Release package

On Windows x64, run `scripts/Publish-Windows.ps1` to build the Rust engine and
the framework-dependent .NET host. The generated ZIP is written under
`artifacts/` and requires the .NET 10 runtime.
