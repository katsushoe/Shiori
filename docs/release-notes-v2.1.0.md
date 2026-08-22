# Shiori v2.1.0

Shiori v2.1.0 centralizes workspace data and makes full indexing resumable.

## Added

- Index construction records completed directories in SQLite.
- The server detects interrupted generations at startup and resumes them in
  the background while keeping the last complete index searchable.

## Changed

- Registered workspaces and every workspace file index now share one central
  SQLite database.
- Workspace authorization no longer depends on an environment variable.
- Removing a workspace also removes its index and interrupted work records.
