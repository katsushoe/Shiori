# ADR 0004: Unified SQLite workspace registry and indexes

## Status

Accepted.

## Context

Shiori previously stored CLI registrations in `workspaces.json`, authorized MCP
paths from `SHIORI_ALLOWED_WORKSPACES`, and kept one index database file per
workspace. These sources could diverge. Separate index files also duplicate
schema and connection lifecycle management even though every index row already
carries a workspace ID.

## Decision

Store every registered workspace, index state, and file index in the single
`<data>/shiori.db` SQLite database. The `Workspaces` table stores stable IDs,
normalized paths, display names, and timestamps. IDs and paths are unique;
ambiguous display-name operations require an ID or absolute path. Index state and file rows use
`workspace_id` foreign keys with cascade deletion. `workspace add`,
`workspace list`, the MCP server, diagnostics, and native search engines all use
this database. `SHIORI_ALLOWED_WORKSPACES` is no longer read.

`workspace remove` deletes the selected `Workspaces` row. SQLite foreign-key
cascades delete only that workspace's state and file-index rows in the same
transaction. Source files and other workspace rows are unchanged.

On first database access, existing `workspaces.json` and `workspaces.db`
registries are imported and archived. Every legacy
`indexes/<workspace-id>/shiori.db` is attached, copied transactionally into the
central tables, and deleted only after a successful copy.

## Alternatives

- Keep the environment variable as the authorization source: rejected because
  it duplicates registration state and requires process-level synchronization.
- Keep JSON as the registry: rejected because concurrent and transactional
  updates are weaker.
- Keep one database file per workspace: rejected because row-level workspace
  keys already provide isolation, while separate files complicate migration,
  deletion, diagnostics, and backup.
- Leave index rows after removal: rejected because removal must reclaim managed
  data and users can explicitly add the workspace again to rebuild it.

## Consequences

- Adding or removing a workspace changes the MCP authorization set after server
  restart without editing environment variables.
- Removing a workspace is destructive for its generated index but never for
  workspace source files.
- All engines share one WAL database. SQLite serializes writes while searches
  remain isolated by mandatory `workspace_id` predicates.
- An empty registry prevents `serve` from starting.
- The managed host gains direct `Microsoft.Data.Sqlite` and pinned
  `SQLitePCLRaw.bundle_e_sqlite3` dependencies.

## Security conditions

- Only absolute directories accepted by `workspace add` can enter the registry.
- CLI `--allow` options reject paths absent from `Workspaces`.
- The MCP server opens engines only for paths read from `Workspaces`.
- Foreign-key cascades are limited to rows whose `workspace_id` matches the
  removed registration.
- Bearer tokens and file contents are never stored in the registry.

## Operational conditions

- `SHIORI_DATA_HOME` selects the directory containing the unified `shiori.db`.
- A removed workspace must be added and indexed again before MCP can search it.
- Legacy registries are retained with a `.migrated` suffix for recovery. Legacy
  per-workspace index directories are removed after successful migration.

## Implementation and verification

- Managed tests verify schema creation, legacy registry and index migration,
  and workspace-scoped cascade deletion. Native tests verify that two engines
  share one database without mixing search results.
- README, command, configuration, MCP setup, specification, package inventory,
  and diagnostics must describe SQLite as the sole workspace source of truth.
