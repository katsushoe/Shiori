# Shiori v2.4.0

Shiori v2.4.0 removes every environment variable. The MCP token is generated and
protected automatically, and MCP clients connect through a stdio bridge.

## Changed

- The MCP bearer token is generated on first use and stored in
  `config\mcp-token.bin`, encrypted with Windows DPAPI for the current user.
  `SHIORI_MCP_TOKEN` is no longer read.
- Added `shiori mcp [--port <port>]`, a stdio bridge that reads the protected
  token and relays MCP requests to the local server.
- `shiori config claude` and `shiori config codex` now generate stdio
  configuration that starts `shiori mcp`. No secret is embedded.
- The data directory is fixed to `<install-root>\data`; `SHIORI_DATA_HOME` is
  no longer read.
- Indexing exclusions moved from `SHIORI_EXCLUDE_PATTERNS` to
  `index.exclude_patterns` in `config\shiori.ini`.
- The native engine ABI is now version 6.

## Upgrade notes

- Regenerate Claude Code and Codex entries with `shiori config claude` and
  `shiori config codex`; HTTP entries that use `${SHIORI_MCP_TOKEN}` or
  `bearer_token_env_var` no longer authenticate.
- Move any `SHIORI_EXCLUDE_PATTERNS` value to `index.exclude_patterns` in
  `config\shiori.ini`, then remove the old environment variables.
- Restart `shiori serve` after upgrading.

See [ADR 0005](adr/0005-no-environment-variables.md) for the design rationale.
