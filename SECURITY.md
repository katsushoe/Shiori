# Security

This document describes the supported versions, reporting path, and security
model for Shiori.

## Supported Versions

Security fixes are provided for the latest published release. Users should
upgrade to the version identified by the repository's latest GitHub Release.

## Reporting a Vulnerability

Do not open a public issue for an undisclosed vulnerability. Use GitHub's
private vulnerability reporting for this repository when available, or contact
the repository owner privately through the GitHub profile. Include affected
versions, reproduction conditions, impact, and a minimal proof of concept.
Receipt and remediation timing depend on severity and reproducibility; no fixed
response-time commitment is currently offered.

## Security Model

- Shiori binds its HTTP server to loopback only and rejects non-loopback `Host`
  and `Origin` headers.
- `/mcp` requires a bearer token. Shiori generates a random 256-bit token and
  stores it in `config\mcp-token.bin`, encrypted with Windows DPAPI for the
  current user. Shiori does not read settings or secrets from environment
  variables.
- MCP clients connect through `shiori mcp`, a stdio bridge that reads the
  protected token and relays requests to the loopback endpoint. Client
  configuration files never contain the token.
- Registered workspaces in `shiori.db` are the MCP filesystem authorization
  boundary. The server loads them at startup; `workspace_add` and
  `workspace_remove` change the boundary while it runs.
- Canonical path checks reject traversal and symlink escapes from allowed roots.
- Index operations read directory entries and file metadata but do not open file
  contents. They write only to Shiori's data directory.
- Shiori does not require external network access at runtime or start external
  search and language-server processes.

## Secrets Handling

Shiori creates the MCP token automatically the first time `shiori serve` or
`shiori mcp` runs. Only the Windows user who created it can decrypt it. Never
copy the token into configuration, logs, command history, screenshots, issue
reports, or sample files. To rotate the token after suspected disclosure, stop
the server, delete `config\mcp-token.bin`, and restart the server and MCP
clients; a new token is generated.

## User Responsibilities

- Register only the minimum required directories as workspaces.
- Protect the operating-system account and Shiori's `config`, `logs`, and `data`
  directories from untrusted users.
- Verify package SHA-256 files before installation.
- Keep Shiori and SQLite updated.
- Review client MCP configuration before enabling Shiori in an untrusted project.
