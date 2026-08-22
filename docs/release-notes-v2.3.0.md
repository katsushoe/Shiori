# Shiori v2.3.0

Shiori v2.3.0 completes CLI and MCP feature parity for operations that can run
inside an active MCP host.

## Added

- Added `workspace_add` and `workspace_remove` MCP tools with immediate live
  access-boundary updates.
- Added `index_build` and `index_rebuild` MCP tools.
- Added MCP `doctor`, `config_claude`, and `config_codex` tools.
- Kept all management tools behind the existing bearer-token authentication.

`serve` remains CLI-only because it starts the MCP host itself.
