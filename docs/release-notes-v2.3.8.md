# Shiori v2.3.8

Shiori v2.3.8 helps MCP clients understand when and how to use Shiori safely.

## Added

- Provides server instructions during MCP initialization that explain Shiori's
  purpose, recommended search workflow, file-content limitation, and mutation
  safety rules.
- Provides the `shiori_guide` MCP prompt with practical guidance for searching,
  maintaining indexes, administering workspaces, and respecting the filesystem
  access boundary.

Shiori indexes file metadata only and never modifies or deletes workspace source files.
