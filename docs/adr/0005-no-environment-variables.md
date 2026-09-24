# ADR 0005: No environment variables, DPAPI token, and stdio bridge

## Status

Accepted.

## Context

Shiori read three environment variables: `SHIORI_MCP_TOKEN` (the `/mcp` bearer
token), `SHIORI_DATA_HOME` (the `shiori.db` directory), and
`SHIORI_EXCLUDE_PATTERNS` (extra indexing exclusions). The host also set
`SHIORI_DATA_HOME` for its own process so the Rust engine resolved the same
directory. Users had to generate the token, keep it in a persistent user
environment, and make sure every server and client process inherited it.
Environment variables leak into child processes, are easy to lose between
terminals, and are not a durable configuration mechanism for a service.

## Decision

Shiori reads no environment variables.

- **MCP token:** the first `shiori serve` or `shiori mcp` run generates a
  random 256-bit token (Base64url, 43 characters) and stores it in
  `<install-root>\config\mcp-token.bin`, encrypted with DPAPI
  (`CurrentUser` scope and fixed entropy). The file is written to a temporary
  file and moved into place without overwriting, so concurrent first runs
  converge on one token.
- **Client connection:** MCP clients start `shiori mcp [--port <port>]`, a
  stdio bridge. It reads the protected token and relays each newline-delimited
  JSON-RPC message to `http://127.0.0.1:<port>/mcp`, converting JSON or SSE
  responses back to single stdout lines. Requests may overlap; output writes
  are serialized. The negotiated protocol version is forwarded in
  `MCP-Protocol-Version`. Transport failures and non-success HTTP statuses
  become JSON-RPC errors (`-32000`) for requests and are dropped for
  notifications. `shiori config claude` and `shiori config codex` generate
  stdio configuration that starts the bridge.
- **Data directory:** fixed to `<install-root>\data`. The host passes it to the
  native engine as an FFI argument of `shiori_engine_open`.
- **Exclusions:** `index.exclude_patterns` in `config\shiori.ini`
  (`;`-separated). The host passes the patterns to `shiori_engine_open`, and the
  engine applies them in directory counting and scanning.

The native ABI version changes from 5 to 6 because `shiori_engine_open` gains
the data-root and exclude-pattern arguments.

## Alternatives

- Embed the token in client configuration headers: rejected because `.mcp.json`
  is project-scoped and often committed, and the coding standard forbids
  embedding secrets in generated client configuration.
- Claude Code `headersHelper` plus an embedded Codex token: rejected because
  Codex has no equivalent helper, so the clients would behave asymmetrically.
- Windows Credential Manager: rejected because a DPAPI file offers the same
  per-user protection without an additional API surface and is covered by the
  existing `config` directory backup and ACL guidance.
- Keep `SHIORI_DATA_HOME` as an optional override or move it to `shiori.ini`:
  rejected; the installation layout already defines `data`, and no supported
  scenario requires relocation.

## Consequences

- Users no longer create, persist, or distribute a token.
- Server and clients must run as the same Windows user; another user cannot
  decrypt the token.
- Each MCP client session starts one short-lived `shiori mcp` process.
- Existing `.mcp.json` and Codex entries that use HTTP with
  `${SHIORI_MCP_TOKEN}` or `bearer_token_env_var` stop authenticating and must
  be regenerated.
- Data stored under a custom `SHIORI_DATA_HOME` must be moved to
  `<install-root>\data` manually.
- A native library built for ABI 5 is rejected by the host.

## Security conditions

- The token never appears in client configuration, logs, command lines, or
  environment blocks.
- The HTTP server keeps loopback binding, host filtering, Origin validation,
  and constant-time bearer comparison.
- The bridge connects only to `127.0.0.1` on the configured port.
- The token file is readable only through DPAPI for the creating user; a
  corrupted or foreign file is reported by `shiori doctor` as an error.

## Operational conditions

- Rotation: stop the server, delete `config\mcp-token.bin`, then restart the
  server and MCP clients.
- `shiori doctor` reports `mcp_token` as `warning` before the file exists.
- Changing `index.exclude_patterns` takes effect when a process next opens the
  workspace; restart `shiori serve` after editing it.

## Implementation and verification

- Managed tests cover token creation, reuse, isolation, and rejection of
  non-DPAPI content; bridge relay of JSON and SSE responses, bearer and protocol
  headers, notifications, HTTP errors, and unreachable servers; stdio client
  configuration generation; and `index.exclude_patterns` parsing.
- A native test covers scanning with configured exclude patterns.
- An end-to-end check starts `shiori serve` in an isolated installation root,
  confirms `401` without a token, and relays `initialize`, `tools/list`, and
  `get_version` through `shiori mcp`.
- README, commands, configuration, MCP setup, security policy, architecture,
  and the v2.0 specification describe the token file, the bridge, and the
  absence of environment variables.
