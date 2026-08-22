# Shiori v2.2.0

Shiori v2.2.0 aligns CLI read operations with the MCP read tools.

## Added

- Added `shiori version` corresponding to MCP `get_version`.
- `shiori find` now searches all registered workspaces when `--allow` is
  omitted and accepts repeated `--allow` options for multi-workspace search.
- CLI search now returns the same workspace-tagged results and structured
  per-workspace errors as MCP `search_files`.
