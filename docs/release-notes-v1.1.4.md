# Shiori v1.1.4

Shiori v1.1.4 adds English and Japanese localization and moves Windows
distribution to a WiX Toolset MSI.

## Changes

- Adds English and Japanese CLI resources for help, diagnostics, and errors.
- Lets users select English or Japanese during MSI installation and stores the
  choice in `config/shiori.ini`.
- Replaces the Inno Setup installer with a per-user WiX Toolset MSI while
  retaining the standard `bin`, `config`, `logs`, and `data` layout.
- Returns the four-part assembly version from the MCP `get_version` tool.
