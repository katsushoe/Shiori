# Shiori v1.1.0

Shiori v1.1.0 establishes fast indexed file-name and path discovery as the
primary product capability and adds multi-workspace coordination for AI agents.
Indexed code search and semantic navigation remain available as secondary
capabilities after the relevant files have been located.

## Highlights

- Search one, several, or every allowed workspace through `search_files`.
- Keep independent SQLite databases and lazily opened Native Engines per workspace.
- Run bounded workspace tasks concurrently and return globally limited,
  workspace-tagged results.
- Update selected or all search databases with synchronous `update_indexes`.
- Continue returning successful workspace results when another workspace fails.
- Search syntax trees with Tree-sitter query patterns through `search_ast`.
- Navigate C# definitions, references, implementations, callers, and callees
  through a separately installed language server.

## Compatibility

- Existing single-workspace `search_files` calls remain supported through the
  optional `workspace` argument.
- Native ABI version 2 is required. Replace the managed host and Native DLL
  together when upgrading from v1.0.0.
- Existing per-workspace SQLite databases remain in place and are updated
  incrementally. Use `update_indexes` with `force: true` if a full rebuild is
  desired.

## Platform

The official v1.1.0 package targets Windows x64 and requires the .NET 10
runtime. The server remains loopback-only, bearer-token protected, and limited
to explicitly allowed workspace roots.
