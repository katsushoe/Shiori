# ADR 0002: Multi-workspace search coordination

## Status

Partially superseded by [ADR 0003](0003-file-search-only-index.md).
Multi-workspace file search remains; MCP-triggered index updates are removed.

## Context

One Codex or Claude session can work with several independent directories.
Shiori already isolates each allowed workspace in its own SQLite database, but
each MCP search currently accepts only one workspace. Cross-workspace search
must not require an AI subagent per directory or merge all indexes into one
database.

## Decision

Keep one persistent SQLite database and one lazily opened Native Engine per
workspace. The C# MCP host owns a coordinator that selects allowed workspaces,
fans searches and index updates out as bounded ThreadPool tasks, waits for all
tasks, and merges deterministic results tagged with workspace identity.

Searches may run concurrently across workspace databases. Index updates are
serialized per workspace but may run concurrently for different workspaces.
The `update_indexes` MCP tool performs incremental updates by default and
returns only after every selected workspace has completed. Existing
single-workspace tools remain available for compatibility.

## Alternatives

- One shared SQLite database was rejected because writer contention, failure
  scope, and mandatory workspace filtering outweigh simpler cross-workspace SQL.
- An AI subagent per workspace was rejected because search is deterministic and
  does not justify model latency, token cost, or nondeterministic aggregation.
- One dedicated OS thread per workspace was rejected because configured
  workspaces can outnumber useful concurrent CPU workers.

## Consequences

Cross-workspace requests require fan-out and result aggregation in the MCP
host. Workspace databases remain independently rebuildable and removable.
Overall search latency is bounded by the slowest selected workspace rather than
the sum of all workspace latencies.

## Security conditions

Every selected path must pass the existing canonical allow-list before an
Engine is opened. Responses include workspace identity so equal relative paths
cannot be confused. No task may search outside its Engine root.

## Operational conditions

Concurrency is bounded to avoid exhausting the ThreadPool or storage device.
Cancellation is propagated while queued work is skipped. A failure in one
workspace is returned as a structured per-workspace error without discarding
successful results from other workspaces.

## Implementation and verification

The MCP contract adds multi-workspace `search_files` selection and synchronous
`update_indexes`. Unit tests cover fan-out, workspace tagging, partial failure,
and update completion. README and the product specification describe the
workspace database and coordination model.
