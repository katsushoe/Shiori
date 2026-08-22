# Shiori v2.3.3

Shiori v2.3.3 improves visible indexing and interruption diagnostics on Windows.

## Fixed

- Resumes interrupted indexes in Windows Terminal instead of silently inside the
  MCP server.
- Prints the interruption reason on a separate line when indexing fails.
- Starts Terminal output with the workspace path, then prints one percentage and
  conventional absolute file path per line.
- Emits a final 100% line after a successful index publication.

Workspace registration and indexing never modify or delete source files.
