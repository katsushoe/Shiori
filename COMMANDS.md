# Shiori Commands

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

This is the detailed reference for the Shiori CLI. All successful query and
management commands write JSON to standard output; errors go to standard error.

## Command Groups

| Group | Commands | Description |
| :--- | :--- | :--- |
| [Search](#search-commands) | `find`, `grep`, `search`, `symbol`, `ast`, `outline`, `navigate` | Locate and inspect files or code |
| [Index](#index-commands) | `index build`, `index status`, `index rebuild` | Maintain one workspace index |
| [Workspace](#workspace-commands) | `workspace add`, `workspace list`, `workspace remove` | Maintain CLI registrations |
| [Integration](#integration-commands) | `config claude`, `config codex`, `serve`, `doctor` | Configure and operate MCP |

## Common Options

- `--allow <directory>`: existing absolute workspace root; required by direct
  search, outline, navigation, and index commands.
- `--path <path>`: optional workspace-relative path filter.
- `--limit <1-100>`: maximum results; default `20`.
- Exit code `0` means success. Exit code `1` means invalid input, unavailable
  runtime dependency, failed operation, or an unhealthy required diagnostic.

## Search Commands

Commands: [`find`](#find), [`grep`](#grep), [`search`](#search),
[`symbol`](#symbol), [`ast`](#ast), [`outline`](#outline),
[`navigate`](#navigate).

### `find`

**Purpose:** indexed file-name and path search. **Syntax:** `shiori find <query> --allow
<directory> [--limit <1-100>]`. `query` must be non-empty. **Example:**
`shiori find README --allow F:\Projects\Shiori --limit 10`. Shiori opens the
workspace database and returns `{"results":[{"type":"file","path":"README.md"}]}`.
It never reads outside `--allow`.

### `grep`

**Purpose:** ripgrep-backed text search. **Syntax:** `shiori grep <query> --allow
<directory> [--path <path>] [--glob <glob>] [--regex] [--case-sensitive]
[--context <0-10>] [--limit <1-100>]`. Literal, case-insensitive search is the
default. **Example:** `shiori grep TODO --allow F:\Projects\Shiori --glob *.md`.
Results contain workspace-relative path, one-based line/column, and snippet;
`{"results":[]}` means no match. Regex input is interpreted only with `--regex`.

### `search`

**Purpose:** planned search across file, symbol, and text providers. **Syntax:**
`shiori search <query> --allow <directory> [--path <path>] [--limit <1-100>]`.
**Example:** `shiori search WorkspaceRegistry --allow F:\Projects\Shiori`.
The query planner selects providers, ranks and deduplicates locations, and
returns JSON containing results, selected providers, and recoverable provider
errors. A provider error can coexist with successful results.

### `symbol`

**Purpose:** indexed symbol search. **Syntax:** `shiori symbol <query> --allow
<directory> [--kind <kind>] [--language <language>] [--path <path>]
[--limit <1-100>]`. **Example:** `shiori symbol RunServer --language csharp --allow
F:\Projects\Shiori`. Results include qualified name, kind, language, path, and
one-based location. Filters are exact metadata filters.

### `ast`

**Purpose:** Tree-sitter structural search. **Syntax:** `shiori ast
<tree-sitter-query> --language <language> --allow <directory> [--path <path>]
[--limit <1-100>]`. Supported languages are `c`, `cpp`, `csharp`, `go`, `java`,
`javascript`, `python`, `rust`, and `typescript`. **Example:** `shiori ast
'(class_declaration name: (identifier) @name)' --language csharp --allow
F:\Projects\Shiori`. Output contains capture name, node kind, path, position,
and a bounded snippet. Invalid queries return exit code `1`.

### `outline`

**Purpose:** return indexed symbols in one source file. **Syntax:** `shiori outline
<source-file> --allow <directory>`. The file may be absolute or workspace
relative but must remain inside the workspace. **Example:** `shiori outline
src\Shiori.Cli\Program.cs --allow F:\Projects\Shiori`. Output contains the file
language and ordered symbol tree; an unsupported file returns an empty outline.

### `navigate`

**Purpose:** C# semantic navigation through an external language server. **Syntax:**
`shiori navigate <definition|references|implementations|callers|callees> <file>
--line <one-based> --column <one-based> --allow <directory> [--limit <1-100>]`.
**Example:** `shiori navigate definition src\Shiori.Cli\Program.cs --line 20
--column 18 --allow F:\Projects\Shiori`. Output reports `success`, locations,
and an error when navigation cannot run. `csharp-ls` or OmniSharp is required;
source coordinates are one-based.

## Index Commands

Commands: [`index build`](#index-build), [`index status`](#index-status),
[`index rebuild`](#index-rebuild).

### `index build`

**Purpose:** create or incrementally update one workspace index. **Syntax:** `shiori
index build --allow <directory>`. **Example:** `shiori index build --allow
F:\Projects\Shiori`. Existing metadata and hashes avoid unnecessary parsing;
added, changed, and deleted files update SQLite. Output contains workspace ID,
status, file/symbol counts, versions, and scan timestamps.

### `index status`

**Purpose:** inspect the persistent index without rebuilding it. **Syntax:** `shiori
index status --allow <directory>`. **Example:** `shiori index status --allow
F:\Projects\Shiori`. The same status schema as `index build` is returned;
missing indexes report their unbuilt state and zero counts.

### `index rebuild`

**Purpose:** force a full rescan. **Syntax:** `shiori index rebuild --allow
<directory>`. **Example:** `shiori index rebuild --allow F:\Projects\Shiori`.
Shiori refreshes all indexed file and symbol rows and returns the index status.
This is more expensive than `index build`; use it for recovery or parser changes.

## Workspace Commands

Commands: [`workspace add`](#workspace-add), [`workspace list`](#workspace-list),
[`workspace remove`](#workspace-remove).

### `workspace add`

**Purpose:** register a workspace for CLI discovery and initialize its database.
**Syntax:** `shiori workspace add <absolute-directory>`. **Example:** `shiori workspace
add F:\Projects\Shiori`. Output is the workspace ID, name, and normalized path.
Duplicate IDs are updated; conflicting directory names are rejected. This does
not authorize MCP access.

### `workspace list`

**Purpose:** list registrations. Syntax and example: `shiori workspace list`.
Output is `{"workspaces":[...]}` in stable name/path order. An empty registry
returns an empty array and exit code `0`.

### `workspace remove`

**Purpose:** remove one registration by name, ID, or absolute path. **Syntax:** `shiori
workspace remove <identifier>`. **Example:** `shiori workspace remove Shiori`.
Output is the removed workspace record. Its SQLite database is preserved; an
unknown or ambiguous identifier fails safely.

## Integration Commands

Commands: [`config claude`](#config-claude), [`config codex`](#config-codex),
[`serve`](#serve), [`doctor`](#doctor).

### `config claude`

**Purpose:** generate project-scoped Claude Code MCP JSON. **Syntax:** `shiori config
claude [--port <1-65535>] [--name <server-name>]`; defaults are `39473` and
`shiori`. **Example:** `shiori config claude > .mcp.json`. Output uses Streamable
HTTP and `${SHIORI_MCP_TOKEN}`; it never writes the token value. Server names
allow letters, digits, `_`, and `-` only.

### `config codex`

**Purpose:** generate a Codex MCP TOML section. **Syntax:** `shiori config codex
[--port <1-65535>] [--name <server-name>]`. **Example:** `shiori config codex` and
merge the output into `%USERPROFILE%\.codex\config.toml`. Output sets
`bearer_token_env_var = "SHIORI_MCP_TOKEN"` and never writes its value.

### `serve`

**Purpose:** run the single stateless Streamable HTTP MCP server. **Syntax:** `shiori
serve [--port <1-65535>]`; default port is `39473`. **Example:** `shiori serve
--port 39473`. It binds only to loopback, exposes `/health` and authenticated
`/mcp`, and runs until stopped. `SHIORI_MCP_TOKEN` and
`SHIORI_ALLOWED_WORKSPACES` are required; startup errors return exit code `1`.

### `doctor`

**Purpose:** validate Native ABI, SQLite/FTS5, ripgrep, Tree-sitter, optional C# LSP,
data-directory access, and MCP environment settings. Syntax and example:
`shiori doctor`. Output is `{"status":"ok|warning|error","checks":[...]}`.
Warnings such as a missing optional LSP still return `0`; required runtime errors
return `1`.

## Safety Notes

Canonical workspace checks apply to every file operation. MCP authorization is
controlled only by `SHIORI_ALLOWED_WORKSPACES`; CLI registrations do not grant
access. `index rebuild` is the only intentionally full-rescan command and does
not delete source files.
