# Shiori v2.3.1

Shiori v2.3.1 makes initial MCP workspace indexing visible on Windows.

## Changed

- Changed `workspace_add` so the MCP server directly opens Windows Terminal.
- Runs `shiori index rebuild --allow <workspace>` in the terminal and displays
  directory and file indexing progress to the local user.
- Preserves the existing in-request indexing behavior on non-Windows platforms.

Workspace removal continues to delete only Shiori registration and generated
index rows. It never deletes files from the registered workspace.
