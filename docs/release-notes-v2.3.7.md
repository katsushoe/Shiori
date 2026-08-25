# Shiori v2.3.7

Shiori v2.3.7 makes every file-search outcome explicit per workspace.

## Added

- Returns a workspace summary containing indexed directory and file counts,
  OK/NG result, returned hit count, and index status.
- Returns the same summary as a Markdown table for user-facing MCP responses.
- Requests user confirmation before an MCP client starts an initial index or
  resumes an interrupted index after a search cannot use a published index.

## Changed

- Uses Akatsukisoft for binary company metadata and the MSI manufacturer.
- Describes Shiori as serving AI agents without limiting it to coding agents.
- Embeds all application files in the MSI so it installs without a separate CAB.

Shiori indexes file metadata only and never modifies or deletes workspace source files.
