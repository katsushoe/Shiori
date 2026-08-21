# Shiori Configuration

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

This document is the reference for Shiori runtime configuration. Shiori reads
non-secret product settings from `config\shiori.ini` and operational or secret
settings from environment variables.

## Configuration Directory

The installer and ZIP use `bin`, `config`, `logs`, and `data` below the selected
installation root. Workspace registrations and indexes are stored in `data` by
default. Set `SHIORI_DATA_HOME` to replace that directory.

## File Generation

- The Windows installer creates `config\shiori.ini` with the language selected
  during setup. The default for silent installation is `en-US`; set the public
  MSI property `SHIORI_LANGUAGE=ja-JP` to select Japanese.
- `workspaces.json` is created and updated by `shiori workspace` commands.
- `indexes\<workspace-id>\shiori.db` is created by index operations.
- Claude and Codex configuration snippets are printed by `shiori config`; the
  user decides where to save or merge them.

## Main Settings

### `config\shiori.ini`

```ini
[general]
language=en-US
```

| Setting | Required | Type | Default | Constraint |
| :--- | :--- | :--- | :--- | :--- |
| `general.language` | No | Locale identifier | `en-US` | `en-US` or `ja-JP` |

If the file is absent, Shiori uses `en-US`. An unsupported value is reported as
an error by `shiori doctor`. The configured language is applied when the process
starts and localizes CLI help and user-facing command errors. Command names,
JSON field names, logs, and MCP protocol values remain language-neutral.

### Environment variables

| Setting | Required | Type | Default | Constraint |
| :--- | :--- | :--- | :--- | :--- |
| `SHIORI_MCP_TOKEN` | For `serve` | String | None | At least 32 characters |
| `SHIORI_ALLOWED_WORKSPACES` | For `serve` | Path list | None | Existing absolute directories |
| `SHIORI_DATA_HOME` | No | Absolute path | `<install-root>\data` | Writable directory |
| `SHIORI_EXCLUDE_PATTERNS` | No | Pattern list | None | `;`-separated gitignore-style patterns |
| `SHIORI_CSHARP_LSP_PATH` | No | Absolute file path | Auto-discovery | Existing C# language-server executable |

Environment variables inherited by the `shiori serve` process take effect at
server startup. Restart the server after changing them.

### `SHIORI_MCP_TOKEN`

Bearer token used for `/mcp`. It is a string with no default and must contain at
least 32 characters. Omission prevents `serve` from starting. Keep it secret and
use the same environment variable in the MCP client process.

```powershell
$env:SHIORI_MCP_TOKEN = ([guid]::NewGuid().ToString('N'))
```

### `SHIORI_ALLOWED_WORKSPACES`

Authorization boundary for MCP file access. It is a list of existing absolute
directory paths separated by the OS path-list separator (`;` on Windows). There
is no default; omission prevents `serve` from starting. CLI `--allow` values and
workspace registrations do not expand this list.

```powershell
$env:SHIORI_ALLOWED_WORKSPACES = 'F:\Projects\One;F:\Projects\Two'
```

### `SHIORI_DATA_HOME`

Optional absolute directory for `workspaces.json` and per-workspace SQLite
databases. The default is the `data` directory below the installation root. The
directory is created when needed; the current user must be able to write it.

```powershell
$env:SHIORI_DATA_HOME = 'D:\ShioriData'
```

### `SHIORI_EXCLUDE_PATTERNS`

Optional `;`-separated list of additional gitignore-style patterns. Patterns are
combined with `.gitignore` and Shiori's built-in build/dependency exclusions.
Omission adds no user-defined patterns.

```powershell
$env:SHIORI_EXCLUDE_PATTERNS = 'generated/**;*.min.js'
```

### `SHIORI_CSHARP_LSP_PATH`

Optional absolute path to `csharp-ls` or OmniSharp. When omitted, Shiori searches
`PATH`. It is used only by C# semantic navigation; file and indexed code search
remain available without it.

```powershell
$env:SHIORI_CSHARP_LSP_PATH = 'C:\Tools\csharp-ls.exe'
```

## Profile Settings

Shiori has no named runtime profiles. Claude Code and Codex each use generated
client configuration that points to `http://127.0.0.1:39473/mcp` by default and
reads `SHIORI_MCP_TOKEN` from the environment.

## Samples

```powershell
$env:SHIORI_MCP_TOKEN = ([guid]::NewGuid().ToString('N'))
$env:SHIORI_ALLOWED_WORKSPACES = 'F:\Projects\One;F:\Projects\Two'
$env:SHIORI_EXCLUDE_PATTERNS = 'generated/**;*.min.js'
shiori doctor
shiori serve --port 39473
```
