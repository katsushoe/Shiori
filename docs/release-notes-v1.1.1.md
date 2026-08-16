# Shiori v1.1.1

Shiori v1.1.1 adds a self-contained Windows x64 installer and improves the
first-run documentation.

## Highlights

- Install for the current Windows user with optional PATH registration.
- Uninstall cleanly through Windows Installed apps.
- Run both installer and ZIP distributions without a separate .NET runtime.
- Follow distinct README instructions for installer, ZIP, or source builds.
- Use dedicated `COMMANDS.md` and `CONFIG.md` reference documents.

## Upgrade

Install v1.1.1 over the previous version or replace the extracted ZIP contents.
Workspace registrations and per-workspace SQLite indexes are stored outside the
application directory and remain available.
