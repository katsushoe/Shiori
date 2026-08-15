# Shiori

Shiori is a local-first code search and navigation server for AI coding agents.

The C# host owns MCP, CLI, configuration, and query planning. A Rust Native DLL
owns performance-sensitive search, indexing, SQLite, and Tree-sitter operations
behind a versioned C ABI.

Product version: `0.0.0.0`.

## Commands

```text
dotnet run --project src/Shiori.Cli -- find <query> --allow <directory> [--limit <count>]
dotnet run --project src/Shiori.Cli -- doctor
dotnet run --project src/Shiori.Cli -- serve --port 39473
```

The server exposes one stateless Streamable HTTP endpoint at
`http://127.0.0.1:39473/mcp`. Set `SHIORI_MCP_TOKEN` to a random value of at
least 32 characters and `SHIORI_ALLOWED_WORKSPACES` to a semicolon-separated
list of absolute workspace paths before starting it.

Opening a configured workspace creates its SQLite database under the platform
data directory (`%LOCALAPPDATA%\Shiori\indexes\<workspace-id>\shiori.db` on
Windows). The `workspace_list` MCP Tool returns registration and schema status.

Build `native/shiori-engine` as a `cdylib` and place the resulting
`shiori_engine` native library beside the managed executable. Search operations
never read outside the canonical `--allow` workspace.
