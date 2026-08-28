# Changelog

All notable changes to Shiori are documented in this file.

## [Unreleased]

## [2.3.9] - 2026-08-29

### Added

- Added `elapsedMilliseconds` to `search_files` responses so AI clients can
  report Shiori's internal indexed-search duration separately from MCP latency.

### Fixed

- Corrected the WiX dialog sequence used by interactive MSI installations.
- Added compliant per-user component key paths and directory cleanup metadata
  so MSI validation no longer reports ICE03, ICE38, or ICE64 errors.

## [2.3.8] - 2026-08-26

### Added

- Added MCP server instructions that explain Shiori's purpose, recommended
  search workflow, content-search limitation, and mutation safety rules.
- Added the `shiori_guide` MCP prompt with practical search, index maintenance,
  workspace administration, and safety guidance.

## [2.3.7] - 2026-08-25

### Added

- Added a per-workspace search summary with indexed directory and file counts,
  OK/NG result, returned hit count, index status, and a Markdown table.
- Added structured confirmation actions when an initial index must be built or
  an interrupted index must be resumed before searching.

### Changed

- Changed product and installer publisher metadata to Akatsukisoft.
- Broadened the documented target from AI coding agents to AI agents.

### Fixed

- Embedded the application cabinet in the MSI so the installer is a complete
  standalone release asset.

## [2.3.6] - 2026-08-23

### Changed

- Changed localized index progress output to show percentage,
  completed/total directories, and the current absolute file path.

## [2.3.5] - 2026-08-23

### Added

- Added the persisted indexed directory count to CLI and MCP index status output.

### Changed

- Changed CLI index status, build, and rebuild commands to use one positional
  workspace argument while retaining the legacy `--allow` form for compatibility.

## [2.3.4] - 2026-08-23

### Fixed

- Added a localized completion message with the indexed file count after an
  index is successfully published in Windows Terminal.
## [2.3.3] - 2026-08-23

### Fixed

- Changed interrupted Windows indexes to resume in Windows Terminal and ensured
  indexing errors appear on a separate visible console line.
- Changed Terminal indexing output to start with the workspace path and then show
  one percentage and absolute file path per line.

## [2.3.1] - 2026-08-23

### Changed

- Changed MCP workspace registration on Windows to launch the initial index in
  Windows Terminal so progress remains visible to the local user.

## [2.3.0] - 2026-08-23

### Added

- Added authenticated MCP tools for workspace add/remove, index build/rebuild,
  diagnostics, and Claude Code/Codex configuration generation.
- Updated the live workspace access boundary immediately after MCP workspace
  registration changes.

## [2.2.0] - 2026-08-23

### Added

- Added CLI `version` and multi-workspace `find` support matching all MCP
  read tools and their structured search response.

## [2.1.1] - 2026-08-22

### Fixed

- Allowed the MCP server to start with no registered workspaces while keeping
  unregistered workspace access denied.

## [2.1.0] - 2026-08-22

### Added

- Added persistent directory checkpoints and automatic background resumption
  of interrupted index generations when the server restarts.

### Changed

- Replaced environment-variable workspace authorization, the JSON registry,
  and per-workspace database files with one central SQLite database.
- Changed `workspace remove` to cascade-delete only the selected workspace's
  generated index rows.

## [2.0.1] - 2026-08-22

### Changed

- Changed `workspace add` to automatically rebuild the workspace index while
  displaying directory-level progress in the console.

## [1.2.0] - 2026-08-21

### Added

- Added bounded Git-aware ranking that favors tracked and recently changed
  files while preserving search match quality and non-Git fallback behavior.

## [1.1.4] - 2026-08-21

### Added

- Added English and Japanese CLI resources for help, diagnostics, and errors.
- Added installer language selection and persisted the selected language in
  `config/shiori.ini`.

### Changed

- Replaced the Inno Setup Windows installer with a WiX Toolset MSI
  (`installer/Shiori.wxs`), keeping the same per-user install location,
  `bin`/`config`/`logs`/`data` layout, and PATH registration.
- Changed `get_version` to return the four-part assembly version.
- Updated the WiX build to accept the WiX Toolset v7 OSMF terms explicitly and
  use UTF-8 for installer text.

## [1.1.3] - 2026-08-17

### Added

- `get_version` MCP tool returning the running Shiori server name and version.

### Documentation

- Added package and security references required by the shared documentation standard.
- Reworked MCP client setup around explicit values, scope, alternatives, and staged verification.
- Standardized README sections, command labels, and specification heading hierarchy.

## [1.1.2] - 2026-08-17

### Changed

- Standardized Windows installations and ZIP packages on `bin`, `config`,
  `logs`, and `data` directories beneath the selected installation root.
- Changed the installed default workspace registry and index location to the
  installation `data` directory while retaining `SHIORI_DATA_HOME` overrides.
- Added writable standard-directory diagnostics to `shiori doctor`.
- Bundled ripgrep 15.2.0 in Windows packages and made the native engine prefer
  the bundled executable before falling back to `PATH`.

## [1.1.1] - 2026-08-17

### Added

- Windows x64 installer with per-user installation, optional PATH registration,
  and clean uninstall support.
- Dedicated CLI command and runtime configuration reference documents.

### Changed

- Windows ZIP and installer packages are self-contained and no longer require a
  separately installed .NET runtime.
- Reorganized the README around installation, initial configuration, and the
  first index build.

## [1.1.0] - 2026-08-17

### Added

- Multi-workspace file-name and path search with bounded parallel coordination.
- Synchronous `update_indexes` MCP tool for selected or all allowed workspaces.
- Tree-sitter AST pattern search for all supported parser languages.
- C# definition, reference, implementation, caller, and callee navigation through LSP.
- Workspace-tagged results and structured per-workspace partial failures.

### Changed

- Positioned indexed file search as Shiori's primary capability, with code search and navigation as secondary capabilities.
- Native ABI advanced to version 2 for AST search support.
- MCP file search can target one, several, or all configured workspaces.

## [1.0.0] - 2026-08-16

### Added

- Streamable HTTP MCP server with bearer-token authentication.
- File, text, symbol, outline, index, workspace, and unified search tools.
- Persistent SQLite indexing, FTS5, Tree-sitter symbols, and file watching.
- C# host with a versioned Rust Native DLL search engine.
- Claude Code and Codex configuration generation and verified integration.
- Windows diagnostics and framework-dependent x64 release packaging.
