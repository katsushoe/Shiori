# Shiori MCP Setup

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

This guide connects a locally running Shiori server to an AI coding agent. For
all environment variables, see [CONFIG.md](CONFIG.md); for CLI details, see
[COMMANDS.md](COMMANDS.md).

## Prerequisites

- Install Shiori using the installer, ZIP, or source instructions in
  [README.md](README.md).
- Choose one or more existing absolute workspace directories.
- Open a new terminal if the installer added Shiori to `PATH`.

## Configure the Server

Create a bearer token of at least 32 characters and authorize the workspace
roots. Windows uses `;` between paths.

```powershell
$env:SHIORI_MCP_TOKEN = ([guid]::NewGuid().ToString('N'))
$env:SHIORI_ALLOWED_WORKSPACES = 'F:\Projects\One;F:\Projects\Two'
shiori doctor
```

The client and server processes must receive the same token. Do not write the
token into a committed configuration file. `workspace add` does not grant MCP
access; only `SHIORI_ALLOWED_WORKSPACES` defines the server boundary.

## Build the Initial Indexes

Build one persistent SQLite index per workspace:

```powershell
shiori index build --allow F:\Projects\One
shiori index build --allow F:\Projects\Two
```

Later builds are incremental. MCP clients can call `update_indexes` for selected
or all authorized workspaces and receive a response after every update finishes.

## Start the Server

```powershell
shiori serve --port 39473
```

The process listens only on loopback. Its MCP endpoint is
`http://127.0.0.1:39473/mcp`; `http://127.0.0.1:39473/health` is the unauthenticated
local health endpoint. Keep the server process running while clients use Shiori.

## Claude Code

Save or merge this complete project-scoped configuration as `.mcp.json` in the
Claude Code project root:

```json
{
  "mcpServers": {
    "shiori": {
      "type": "http",
      "url": "http://127.0.0.1:39473/mcp",
      "headers": {
        "Authorization": "Bearer ${SHIORI_MCP_TOKEN}"
      }
    }
  }
}
```

`shiori` is the client-visible server name, `type` selects HTTP transport,
`url` must use the port passed to `shiori serve`, and the authorization header
reads the token from the client process environment. Do not replace the
environment reference with the secret value.

As a convenience, Shiori can generate the same JSON:

```powershell
shiori config claude > .mcp.json
```

Use redirection only when creating a new file because it overwrites the file. If
`.mcp.json` already exists, merge the generated `mcpServers.shiori` entry instead.
Start Claude Code from an environment containing `SHIORI_MCP_TOKEN`, restart or
reload it after changing the file, and inspect `/mcp`.

## Codex

Add this complete server section to `%USERPROFILE%\.codex\config.toml`:

```toml
[mcp_servers.shiori]
url = "http://127.0.0.1:39473/mcp"
bearer_token_env_var = "SHIORI_MCP_TOKEN"
```

`shiori` is the client-visible server name. An `url` selects HTTP transport and
must use the port passed to `shiori serve`; no local start command is needed
because Shiori runs separately. `bearer_token_env_var` tells Codex to read the
bearer token from its process environment without storing the secret in TOML.

As a convenience, Shiori can generate the same TOML section:

```powershell
shiori config codex
```

Merge the output without replacing other Codex settings, ensure Codex receives
`SHIORI_MCP_TOKEN`, then restart Codex or start a new task.

## Verify the Connection

1. Confirm that the client lists the Shiori MCP server and its tools.
2. Call `workspace_list` and confirm the authorized roots.
3. Call `search_files` with a known filename.
4. Call `update_indexes` and confirm that each requested workspace completes.

`search_files` may target one workspace, several workspace paths, or all allowed
workspaces. Results include workspace identity so clients can distinguish equal
relative paths from different roots.

## Troubleshooting

### Unauthorized response

Confirm that the server and client inherited the same `SHIORI_MCP_TOKEN`. The
token must contain at least 32 characters. Restart both processes after changes.

### Workspace rejected or missing

Use an existing absolute path in `SHIORI_ALLOWED_WORKSPACES`, separated with `;`
on Windows. Restart the server after changing the list. CLI registrations do not
authorize MCP access.

### Connection refused

Confirm that `shiori serve` is still running, the configured ports match, and
the client URL is `http://127.0.0.1:<port>/mcp`.

### Search returns stale results

Call `update_indexes` or run `shiori index build --allow <workspace>`. Use
`index rebuild` only when a full rescan is required.

### Semantic navigation is unavailable

File search and indexed code search do not require a language server. For C#
semantic navigation, install `csharp-ls` or OmniSharp and optionally set
`SHIORI_CSHARP_LSP_PATH` to its absolute executable path.
