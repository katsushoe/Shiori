# Shiori v2.3.5

Shiori v2.3.5 adds fast directory-count reporting and clearer CLI index commands.

## Added

- Adds nullable `indexed_directories` to CLI index status output and optional
  `indexedDirectories` to the corresponding MCP response.
- Persists the completed directory count from SQLite index progress during
  successful publication, so status requests never rescan the workspace.

## Changed

- Uses `shiori index status <workspace>`, `shiori index build <workspace>`, and
  `shiori index rebuild <workspace>` as the documented CLI syntax.
- Continues accepting the legacy `--allow <workspace>` syntax for compatibility.

Workspace registration and indexing never modify or delete source files.
