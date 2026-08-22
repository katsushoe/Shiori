# Architecture

## Managed host and native engine

Shiori uses a C# host and a Rust Native DLL.

The C# host owns:

- a single localhost Streamable HTTP MCP endpoint and tool contracts
- CLI and configuration
- file-search response shaping
- native engine loading and ABI compatibility checks

The Rust engine owns:

- workspace boundary enforcement
- file-name and relative-path search
- SQLite connections, schema, and indexes
- Gitignore-aware metadata-only indexing with bounded batches

The boundary is a versioned C ABI. Calls operate on coarse search requests and
structured result buffers. Rust allocates result buffers and C# always returns
them through `shiori_engine_free_buffer`. Engine instances are opaque handles
closed through `shiori_engine_close`. No panic may cross the ABI boundary.

SQLite connections remain entirely owned by the Rust engine. Index progress
crosses the ABI through a synchronous directory-completion callback. See
[ADR 0003](adr/0003-file-search-only-index.md) for the v2 responsibility and
streaming-index decision.

Each canonical workspace has a stable SHA-256 ID and an isolated SQLite
database. Schema migrations run transactionally when the native engine opens
the workspace. Connections use WAL, `synchronous=NORMAL`, foreign keys, and
memory-backed temporary storage.

## Streamable HTTP security

The MCP endpoint is stateless and binds only to `127.0.0.1`. Host filtering,
loopback Origin validation, and a bearer token protect the endpoint from DNS
rebinding and unauthorized local callers. The native engine registry opens only
exact workspace paths listed in `SHIORI_ALLOWED_WORKSPACES`.
