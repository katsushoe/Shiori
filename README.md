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
```

Build `native/shiori-engine` as a `cdylib` and place the resulting
`shiori_engine` native library beside the managed executable. Search operations
never read outside the canonical `--allow` workspace.
