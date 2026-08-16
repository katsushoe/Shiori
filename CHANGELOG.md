# Changelog

All notable changes to Shiori are documented in this file.

## [1.1.0] - 2026-08-17

### Added

- Multi-workspace file-name and path search with bounded parallel coordination.
- Synchronous `update_indexes` MCP tool for selected or all allowed workspaces.
- Tree-sitter AST pattern search for all supported parser languages.
- C# definition, reference, implementation, caller, and callee navigation through LSP.
- Workspace-tagged results and structured per-workspace partial failures.

### Changed

- Positioned indexed file search as Shiori's primary capability, with code search and navigation as secondary capabilities.
- Native ABI advanced to version 2 for AST search support.
- MCP file search can target one, several, or all configured workspaces.

## [1.0.0] - 2026-08-16

### Added

- Streamable HTTP MCP server with bearer-token authentication.
- File, text, symbol, outline, index, workspace, and unified search tools.
- Persistent SQLite indexing, FTS5, Tree-sitter symbols, and file watching.
- C# host with a versioned Rust Native DLL search engine.
- Claude Code and Codex configuration generation and verified integration.
- Windows diagnostics and framework-dependent x64 release packaging.
