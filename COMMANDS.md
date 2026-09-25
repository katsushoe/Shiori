# Shiori command reference

Shiori v2 provides file-name and path discovery only. Index operations read
directory entries and file metadata without opening file contents.

## CLI

### `shiori version`

Returns the same server name and four-part version as MCP `get_version`.

### `shiori find`

```powershell
shiori find [<query>] [--name-starts-with <text>] [--name-ends-with <text>] [--allow <absolute-directory> ...] [--limit <1-100>]
```

Searches the ready SQLite file index. `<query>` matches a fragment anywhere in the
file name or relative path. `--name-starts-with` and `--name-ends-with` match the
start and end of the file name only, for example `--name-starts-with Thunderbird`
or `--name-ends-with .cs`. Specify at least one condition; every supplied
condition must match. Matching is case-insensitive for ASCII letters, and `%`
and `_` are treated literally.
Omitting `--allow` searches all registered workspaces. Repeat `--allow` to
search a selected set. Results and per-workspace errors match MCP `search_files`.
Every response includes a workspace summary and a Markdown table with workspace
name, indexed directory and file counts, OK/NG result, returned hit count, and
index status. A workspace without a published index returns NG and an explicit
confirmation action; indexing starts only after the user approves a subsequent
`index_build` call.

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
shiori config init [--language <en-US|ja-JP>]
shiori serve [--port <1-65535>]
shiori mcp [--port <1-65535>]
```

`serve` starts the loopback MCP server and creates the protected MCP token on
first run. `mcp` is the stdio bridge that MCP clients start: it reads the
protected token and relays newline-delimited JSON-RPC between stdin/stdout and
`http://127.0.0.1:<port>/mcp`. `config claude` and `config codex` print client
configuration that starts `shiori mcp`. `config init` creates `config\shiori.ini`
with the given language only when the file does not exist; the installer runs
it so upgrades keep existing settings.

`workspace add` registers the workspace and automatically rebuilds its index
while showing progress in the console. Registered workspaces are the MCP access
boundary. `workspace remove` deletes both the registration and its index.
If a name matches multiple migrated workspaces, use the workspace ID or absolute path.

## MCP tools

- `get_version`: returns the running Shiori name and version.
- `workspace_list`: lists allowed workspaces and their databases.
- `index_status`: returns one allowed workspace's index state.
- `search_files`: searches one, several, or all allowed workspaces and returns a
  workspace summary table. `query`, `nameStartsWith`, and `nameEndsWith` match as
  in `shiori find`; at least one is required. Clients must show the table to the user. If the response
  requests index build or resume confirmation, the client asks the user before
  calling `index_build`; `search_files` itself remains read-only.
- `workspace_add`: registers a directory and adds it to the live access boundary.
  On Windows, the MCP server directly opens Windows Terminal and displays initial
  indexing progress. On other platforms, the initial index runs in the MCP request.
  Interrupted Windows indexes also resume in Terminal at the next MCP server start,
  and an indexing error prints its interruption reason on a separate line.
  The first line identifies the workspace; every subsequent progress line contains
  a percentage and one absolute file path.
- `workspace_remove`: removes a workspace and its index rows from the live server.
- `index_build`: builds and atomically publishes a workspace index.
- `index_rebuild`: rebuilds and atomically replaces a workspace index.
- `doctor`: returns native, SQLite, directory, settings, token, and workspace checks.
- `config_claude`: generates Claude Code MCP configuration.
- `config_codex`: generates Codex MCP configuration.

The four lookup tools and the three diagnostics/configuration tools are
read-only. Workspace and index management tools modify local SQLite state and
require the same bearer token as every MCP request. `workspace_add` can expand
the server's filesystem access boundary. `serve` and `mcp` remain CLI-only
because they start the MCP host and its transport bridge.
