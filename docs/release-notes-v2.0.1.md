# Shiori v2.0.1

Shiori v2.0.1 automatically prepares newly registered workspaces for search.

## Changed

- `shiori workspace add <absolute-directory>` now rebuilds the workspace index
  immediately after registration.
- Directory counting, indexing progress, and completion details are displayed
  in the console during the automatic rebuild.
- Workspace registration still does not extend `SHIORI_ALLOWED_WORKSPACES` or
  grant MCP access.
