# Shiori MCP Setup

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

This guide connects a locally running Shiori server to an AI coding agent. For
all settings, see [CONFIG.md](CONFIG.md); for CLI details, see
[COMMANDS.md](COMMANDS.md).

## Values and Placeholders

| Value | How to obtain it | Example | Change when |
| :--- | :--- | :--- | :--- |
| Workspace path | Copy an existing absolute directory path | `F:\Projects\One` | Authorizing a different workspace |
| Port | Choose an unused loopback TCP port | `39473` | The default port is unavailable |
| Server name | Choose a client-visible identifier | `shiori` | Registering multiple Shiori servers |

Values shown in angle brackets, such as `<workspace>`, are placeholders. Replace
them with real values; do not enter the angle brackets.

## Prerequisites

- Install Shiori using the installer, ZIP, or source instructions in
  [README.md](README.md).
- Choose one or more existing absolute workspace directories.
- Open a new terminal if the installer added Shiori to `PATH`.

## Authentication

Register the workspace roots that MCP may access:

```powershell
shiori workspace add F:\Projects\One
shiori workspace add F:\Projects\Two
shiori doctor
```

No token setup is required. The first `shiori serve` or `shiori mcp` run creates
a random bearer token in `config\mcp-token.bin`, encrypted with Windows DPAPI
for the current user. Clients never see the token: they start the `shiori mcp`
stdio bridge, which reads it and relays requests to the server. Run the server
and the clients as the same Windows user. The central `Workspaces` table
defines the server boundary.

## Start the Server

```powershell
shiori serve --port 39473
```

The process listens only on loopback. Its MCP endpoint is
`http://127.0.0.1:39473/mcp`; `http://127.0.0.1:39473/health` is the unauthenticated
local health endpoint. Keep the server process running while clients use Shiori.

On initialization, Shiori provides server instructions that describe its purpose,
recommended search workflow, content-search limitation, and mutation safety rules.
MCP clients that support prompts can also open `shiori_guide` for a practical guide
to searching, index maintenance, and workspace administration.

## Register Clients

Client registration controls where a client can discover Shiori. Filesystem
access remains restricted to workspaces registered with `shiori workspace add`.
Both clients start `shiori mcp`, which requires `shiori` on `PATH`.

### Claude Code (recommended)

Save or merge this complete project-scoped configuration as `.mcp.json` in the
Claude Code project root:

```json
{
  "mcpServers": {
    "shiori": {
      "type": "stdio",
      "command": "shiori",
      "args": ["mcp", "--port", "39473"]
    }
  }
}
```

`shiori` is the client-visible server name, `type` selects stdio transport, and
`--port` must match the port passed to `shiori serve`. The file contains no
secret and can be committed.

Restart or reload Claude Code after changing the file, then inspect `/mcp`.

### Claude Code generator (alternative)

Run this from the Claude Code project root to generate the same project-scoped
`.mcp.json` content:

```powershell
shiori config claude > .mcp.json
```

Use redirection only when creating a new file because it overwrites the file. If
`.mcp.json` already exists, merge the generated `mcpServers.shiori` entry instead.
Restart or reload Claude Code after changing the file, and inspect `/mcp`.

### Codex (recommended)

Add this complete server section to `%USERPROFILE%\.codex\config.toml`:

```toml
[mcp_servers.shiori]
command = "shiori"
args = ["mcp", "--port", "39473"]
```

`shiori` is the client-visible server name. `command` and `args` start the
stdio bridge; `--port` must match the port passed to `shiori serve`. The TOML
contains no secret.

Restart Codex or start a new task after changing the file.

### Codex generator (alternative)

Run this command to print the same user-scoped TOML section:

```powershell
shiori config codex
```

Merge the output without replacing other Codex settings, then restart Codex or
start a new task.

## Multiple Workspaces

Register each workspace. Every index is stored in the unified SQLite database:

```powershell
shiori workspace add F:\Projects\One
shiori workspace add F:\Projects\Two
```

Run the CLI again whenever the file index must be refreshed. MCP tools are
read-only and never start index operations.
Client scope and workspace authorization are independent: one registered client
can access only paths stored in the central `Workspaces` table.

## Verify the Connection

Stop at the first failed stage and resolve it before continuing.

1. Open `http://127.0.0.1:39473/health`. Pass: HTTP `200` with healthy status.
2. Confirm that the client lists the `shiori` server and its tools. Pass: no
   connection or authentication error.
3. Call read-only `workspace_list`. Pass: every expected authorized root is
   returned and no unauthorized root appears.
4. Call read-only `search_files` with a known filename. Pass: the expected file
   is returned with its workspace identity.
5. Run `shiori doctor`. Pass: required checks are `ok`.

`search_files` may target one workspace, several workspace paths, or all allowed
workspaces. Results include workspace identity so clients can distinguish equal
relative paths from different roots.

## Troubleshooting

### Unauthorized response

Confirm that the server and the MCP client run as the same Windows user, so the
bridge can decrypt the token the server uses. If the token was rotated while the
server was running, restart the server and the clients. `shiori doctor` reports
`mcp_token` as `error` when the current user cannot decrypt the token file.

### Workspace rejected or missing

Register an existing absolute directory with `shiori workspace add <path>`, then
restart the server. Use `shiori workspace list` to inspect the authorization set.

### Connection refused

Confirm that `shiori serve` is still running and that the `--port` value in the
client configuration matches the server port. The bridge returns a JSON-RPC
error when it cannot reach the server.

### Search returns stale results

Run `shiori index build --allow <workspace>`. Use `index rebuild` when an
explicit replacement is required.
