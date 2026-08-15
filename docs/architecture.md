# Architecture

## Managed host and native engine

Shiori uses a C# host and a Rust Native DLL.

The C# host owns:

- MCP transports and tool contracts
- CLI and configuration
- query planning and response shaping
- native engine loading and ABI compatibility checks

The Rust engine owns:

- workspace boundary enforcement
- file, text, symbol, and AST search
- SQLite connections, schema, and indexes
- Tree-sitter parsers and incremental indexing

The boundary is a versioned C ABI. Calls operate on coarse search requests and
structured result buffers. Rust allocates result buffers and C# always returns
them through `shiori_engine_free_buffer`. Engine instances are opaque handles
closed through `shiori_engine_close`. No panic may cross the ABI boundary.

SQLite connections and Tree-sitter objects must not be shared across the ABI.
They remain entirely owned by the Rust engine.
