# ADR 0001: Managed lazy-start LSP engine

## Status

Superseded by [ADR 0003](0003-file-search-only-index.md) in v2.0.0.

## Context

Semantic definition, reference, implementation, and call-hierarchy navigation
requires a language server. Search and indexing must continue to work when no
language server is installed.

## Decision

The C# host owns language-server discovery, process lifecycle, and stdio
JSON-RPC. A server is started lazily for a workspace and language only when a
semantic-navigation tool is called. C# discovery first checks
`SHIORI_CSHARP_LSP_PATH`, then `csharp-ls` and `OmniSharp` on `PATH`.

The initial implementation sequence is discovery and diagnostics, JSON-RPC
transport and lifecycle, then Definition, References, Implementations, and call
hierarchy. An unavailable LSP returns a structured `LSP_UNAVAILABLE` result and
preserves Tree-sitter plus text-search fallback availability.

## Alternatives

- Native Rust LSP ownership was rejected because process and JSON-RPC lifecycle
  belong to the host and do not benefit from crossing the C ABI.
- Eager startup was rejected because it increases startup cost for search-only
  clients and every configured workspace.
- Bundling one language server was rejected because it increases package size
  and couples Shiori releases to third-party server distribution terms.

## Consequences

Semantic navigation depends on a separately installed language server and its
language-specific behavior. The host must manage initialization, cancellation,
timeouts, shutdown, and crashed-process recovery. Existing indexed search stays
available independently.

## Security conditions

Executables are started directly without shell interpolation. Workspace roots
must pass the existing allow-list boundary before being sent to a language
server. Server stderr must not expose source content or bearer tokens in normal
logs.

## Operational conditions

`doctor` reports discovery without starting a server. Processes start on first
semantic request, are reused per workspace, and receive bounded shutdown before
forced termination. Missing or failed servers degrade to structured errors.

## Implementation and verification

Unit tests cover configured-path precedence, PATH discovery, and absence.
Transport tests use a fake stdio server before any real C# server integration.
README documents configuration; MCP and CLI navigation contracts are added with
their corresponding implementation tests.
