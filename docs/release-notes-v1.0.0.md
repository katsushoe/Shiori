# Shiori v1.0.0

Shiori v1.0.0 is the first stable release of the local-first code search and
navigation MCP server for AI coding agents.

## Highlights

- One authenticated Streamable HTTP endpoint for all MCP clients.
- Ranked unified search across files, symbols, and source text.
- Persistent incremental SQLite index with Tree-sitter symbol extraction.
- Claude Code and Codex configuration generators.
- C# MCP host with a replaceable Rust Native DLL engine.

## Platform

The v1 release package targets Windows x64 and requires the .NET 10 runtime.
The server binds to loopback by default and requires a bearer token of at least
32 characters plus an explicit allowed-workspace list.

## Upgrade notes

This is the first stable release. Index databases created by development builds
can be rebuilt with `shiori index rebuild --allow <directory>` if necessary.
