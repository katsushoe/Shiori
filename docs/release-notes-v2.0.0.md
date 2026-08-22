# Shiori v2.0.0

Shiori v2.0.0 focuses exclusively on fast local file discovery.

## Breaking changes

- Removes text, symbol, AST, outline, unified code search, and LSP navigation.
- Removes the `reindex` and `update_indexes` MCP tools; MCP is read-only.
- Removes Tree-sitter, ripgrep, and language-server dependencies.
- Requires rebuilding v1.x indexes into the v2 metadata-only schema.

## Indexing

- Does not open file contents or calculate content hashes.
- Counts included directories before indexing.
- Shows directory-level console progress and completion percentage.
- Streams metadata to SQLite in bounded batches instead of retaining a complete
  workspace scan in memory.
- Publishes completed generations atomically and preserves the previous index
  after failures or interruptions.
