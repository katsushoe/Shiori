# DOCUMENTS.md Version

2026.08.17

This document is the source of truth for Shiori's repository document layout.

## Placement Policy

Public user and developer documentation is tracked at the repository root or
under `docs/`. Generated packages belong under ignored `artifacts/`.

## Project Directories

| Path | Tracked | Purpose |
| :--- | :--- | :--- |
| `src/` | Yes | Managed C# host and libraries |
| `native/` | Yes | Rust native search engine |
| `tests/` | Yes | Managed test projects |
| `installer/` | Yes | Windows installer source |
| `scripts/` | Yes | Build and packaging scripts |
| `docs/` | Yes | Public design and release documents |
| `artifacts/` | No | Generated packages and checksums |

## Documents

| Document | Canonical path | Tracked | Purpose |
| :--- | :--- | :--- | :--- |
| README | `README.md` | Yes | Product entry, installation, and first-run flow |
| Command reference | `COMMANDS.md` | Yes | Detailed CLI syntax and behavior |
| Configuration reference | `CONFIG.md` | Yes | Runtime settings and samples |
| Specification | `docs/specification.md` | Yes | External and implementation specification |
| Architecture | `docs/architecture.md` | Yes | Component architecture |
| Progress | `PROGRESS.md` | Yes | Current completion and remaining work |
| Changelog | `CHANGELOG.md` | Yes | Released changes |
| Release checklist | `RELEASE_CHECKLIST.md` | Yes | Release verification state |

Local notes and secrets are not canonical project documents and must not be
committed.
