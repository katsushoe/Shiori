# Shiori command reference

Shiori v2 provides file-name and path discovery only. Index operations read
directory entries and file metadata without opening file contents.

## CLI

### `shiori version`

Returns the same server name and four-part version as MCP `get_version`.

### `shiori find`

```powershell
shiori find <query> [--allow <absolute-directory> ...] [--limit <1-100>]
```

Searches the ready SQLite file index for a file-name or relative-path fragment.
Omitting `--allow` searches all registered workspaces. Repeat `--allow` to
search a selected set. Results and per-workspace errors match MCP `search_files`.

### `shiori index build`

```powershell
shiori index build --allow <absolute-directory>
```

Counts included directories, prints `completed/total (percent)` progress, and
publishes a new index generation. Metadata is streamed to SQLite in bounded
batches. A failed build leaves the previous ready generation searchable.

### `shiori index rebuild`

Uses the same visible, streaming workflow as `index build` and explicitly
replaces the ready generation.

### `shiori index status`

Returns workspace ID, state, file count, index version, and scan timestamps.

### Workspace and server commands

```powershell
shiori workspace add <absolute-directory>
shiori workspace list
shiori workspace remove <name-or-id-or-absolute-directory>
shiori doctor
shiori config claude [--port <1-65535>] [--name <server-name>]
shiori config codex [--port <1-65535>] [--name <server-name>]
shiori serve [--port <1-65535>]
```

`workspace add` registers the workspace and automatically rebuilds its index
while showing progress in the console. Registered workspaces are the MCP access
boundary. `workspace remove` deletes both the registration and its index.
If a name matches multiple migrated workspaces, use the workspace ID or absolute path.

## MCP tools

- `get_version`: returns the running Shiori name and version.
- `workspace_list`: lists allowed workspaces and their databases.
- `index_status`: returns one allowed workspace's index state.
- `search_files`: searches one, several, or all allowed workspaces.
- `workspace_add`: registers a directory and adds it to the live access boundary.
  On Windows, the MCP server directly opens Windows Terminal and displays initial
  indexing progress. On other platforms, the initial index runs in the MCP request.
- `workspace_remove`: removes a workspace and its index rows from the live server.
- `index_build`: builds and atomically publishes a workspace index.
- `index_rebuild`: rebuilds and atomically replaces a workspace index.
- `doctor`: returns native, SQLite, directory, settings, token, and workspace checks.
- `config_claude`: generates Claude Code MCP configuration.
- `config_codex`: generates Codex MCP configuration.

The four lookup tools and the three diagnostics/configuration tools are
read-only. Workspace and index management tools modify local SQLite state and
require the same bearer token as every MCP request. Keep that token restricted:
`workspace_add` can expand the server's filesystem access boundary. `serve`
remains CLI-only because it starts the MCP host itself.
