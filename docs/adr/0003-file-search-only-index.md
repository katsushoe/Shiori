# ADR 0003: File-search-only streaming index

## Status

Accepted for Shiori v2.0.0.

## Context

Shiori v1.x combines file discovery with text, symbol, AST, and LSP code search.
Building the code index reads every file, hashes its contents, parses supported
languages, and accumulates scan results before writing SQLite. Large workspaces
have produced multi-gigabyte peak memory use and long initial builds. File
discovery and code analysis are distinct use cases, while Shiori's primary goal
is simple and predictably fast file discovery.

## Decision

Shiori v2.0.0 indexes file paths and filesystem metadata only. The indexer does
not open regular file contents. Text, symbol, AST, outline, unified code search,
and LSP navigation are removed together with Tree-sitter, ripgrep, and language
server dependencies.

Index construction is an explicit console operation. It first counts included
directories, then reports completed directories as `completed/total (percent)`.
File metadata and seen paths are written to SQLite staging tables in bounded
batches. A short final transaction publishes the completed generation. Failed
or interrupted runs never replace the last ready generation.

The normative behavioral details, batch thresholds, progress rules, migration,
and acceptance criteria are defined in
[`specification-v2.0.ja.md`](../specification-v2.0.ja.md).

## Alternatives

- Keep code search optional: rejected because optional parsers and providers
  retain dependency, configuration, testing, and maintenance complexity.
- Use on-demand code search: rejected because it remains outside the file
  discovery responsibility and has different latency characteristics.
- Accumulate a complete scan in memory: rejected because memory grows with the
  workspace and has already reached unacceptable levels.
- Commit directly to the live index: rejected because cancellation or failure
  could expose a partial index.
- Hold one transaction for the complete scan: rejected because it creates a
  long-running writer and can grow SQLite transaction state excessively.

## Consequences

- v1.x code-search MCP tools and CLI commands are breaking removals.
- v1.x index databases require an explicit rebuild into the v2 schema.
- Index time and memory become proportional to bounded batches rather than file
  contents or total result count.
- Consumers use their editor, agent, or dedicated search tools after Shiori
  locates candidate files.
- Directory counting requires a preliminary traversal before indexing.

## Security conditions

- `SHIORI_ALLOWED_WORKSPACES` remains the MCP authorization boundary.
- Index traversal does not follow symbolic links or directory junctions.
- Excluded paths are neither indexed nor emitted in progress output.
- File contents, queries, and bearer tokens are never persisted by indexing.
- Staging generations are private to one workspace and cannot be searched.

## Operational conditions

- CLI indexing writes progress to its current console; redirected output uses
  newline-delimited records without terminal control sequences.
- Stale staging generations are removed safely on the next index operation.
- SQLite caches and application queues remain bounded.
- A million-file acceptance test limits peak resident-memory growth to 256 MiB.

## Implementation and verification

- Remove code-search contracts, implementations, dependencies, diagnostics, and
  user documentation.
- Replace whole-workspace vectors with a streaming walker and bounded SQLite
  batches.
- Add native and managed tests for exclusions, content-free indexing, progress,
  batching, recovery, and file search.
- Keep README, command references, configuration, security, architecture,
  packaging, and release notes synchronized before release.
