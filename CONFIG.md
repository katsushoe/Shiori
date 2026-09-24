# Shiori Configuration

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

This document is the reference for Shiori runtime configuration. Shiori reads
product settings from `config\shiori.ini` and keeps the MCP token in a
DPAPI-protected file. Shiori does not read environment variables.

## Configuration Directory

The installer and ZIP use `bin`, `config`, `logs`, and `data` below the selected
installation root. Workspace registrations and indexes are stored in `data` by
and are not relocatable.

## File Generation

- The Windows installer runs `shiori config init`, which creates
  `config\shiori.ini` with the language selected during setup only when the file
  does not exist. Upgrades and uninstalls never modify or delete it. The
  default for silent installation is `en-US`; set the public MSI property
  `SHIORI_LANGUAGE=ja-JP` to select Japanese.
- `shiori.db` contains the central `Workspaces` table and every workspace index.
  Legacy `workspaces.json`, `workspaces.db`, and per-workspace databases are
  migrated once.
- Claude and Codex configuration snippets are printed by `shiori config`; the
  user decides where to save or merge them.

## Main Settings

### `config\shiori.ini`

```ini
[general]
language=en-US

[index]
exclude_patterns=generated/**;*.min.js
```

| Setting | Required | Type | Default | Constraint |
| :--- | :--- | :--- | :--- | :--- |
| `general.language` | No | Locale identifier | `en-US` | `en-US` or `ja-JP` |
| `index.exclude_patterns` | No | Pattern list | None | `;`-separated gitignore-style patterns |

If the file is absent, Shiori uses `en-US`. An unsupported value is reported as
an error by `shiori doctor`. The configured language is applied when the process
starts and localizes CLI help and user-facing command errors. Command names,
JSON field names, logs, and MCP protocol values remain language-neutral.

### `index.exclude_patterns`

Optional `;`-separated list of additional gitignore-style patterns. Patterns are
combined with `.gitignore` and Shiori's built-in build/dependency exclusions.
Omission adds no user-defined patterns. The value is read when a process opens
a workspace; restart `shiori serve` after changing it.

### MCP token file

| File | Created by | Protection | Content |
| :--- | :--- | :--- | :--- |
| `config\mcp-token.bin` | First `shiori serve` or `shiori mcp` run | DPAPI, current Windows user | Random 256-bit bearer token |

The server authenticates `/mcp` with this token, and the `shiori mcp` stdio
bridge reads the same file. There is no manual token setting. To rotate the
token, stop the server, delete the file, and restart the server and MCP clients.
`shiori doctor` reports `mcp_token` as `warning` before the file exists and
`error` when the current user cannot decrypt it.

## Profile Settings

Shiori has no named runtime profiles. Claude Code and Codex each use generated
client configuration that starts `shiori mcp --port 39473`; the bridge relays to
`http://127.0.0.1:39473/mcp` with the protected token.

## Samples

```ini
[general]
language=en-US

[index]
exclude_patterns=generated/**;*.min.js
```

```powershell
shiori workspace add F:\Projects\One
shiori workspace add F:\Projects\Two
shiori doctor
shiori serve --port 39473
```
