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

- Shiori binds its HTTP server to loopback only.
- `/mcp` requires a bearer token of at least 32 characters.
- `SHIORI_ALLOWED_WORKSPACES` is the MCP filesystem authorization boundary.
- Canonical path checks reject traversal and symlink escapes from allowed roots.
- CLI workspace registration does not grant MCP access.
- Search and navigation tools read source files; index operations write only to
  Shiori's data directory and do not modify source files.
- Shiori does not require external network access at runtime. Optional external
  language-server processes are started locally.

## Secrets Handling

Generate a unique MCP token, provide it to the server and client through their
process environments, and never commit it to configuration or logs. Do not put
tokens in command history, screenshots, issue reports, or sample files. Rotate
the token after suspected disclosure and restart both server and clients.

## User Responsibilities

- Restrict `SHIORI_ALLOWED_WORKSPACES` to the minimum required directories.
- Protect the operating-system account and Shiori's `config`, `logs`, and `data`
  directories from untrusted users.
- Verify package SHA-256 files before installation.
- Keep Shiori, ripgrep, SQLite, Tree-sitter parsers, and optional language
  servers updated.
- Review client MCP configuration before enabling Shiori in an untrusted project.
