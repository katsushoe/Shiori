# Shiori v2.1.1

Shiori v2.1.1 allows the MCP server to run before any workspace is registered.

## Fixed

- The server no longer exits when the central SQLite database contains no
  registered workspaces.
- Workspace listing and health checks remain available with zero workspaces,
  and searches return no results until a workspace is registered.
- Requests for unregistered workspace paths remain denied.
