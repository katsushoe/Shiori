# Shiori

Shiori is a local-first code search and navigation server for AI coding agents.

The current implementation is the first Phase 1 slice: workspace isolation,
file search, text search through ripgrep, and a CLI surface.

Product version: `0.0.0.0`. Cargo's package version uses the compatible
three-part representation `0.0.0`; the four-part product version is retained
in `package.metadata.shiori.version`.

## Commands

```text
shiori find <query> --allow <directory> [--limit <count>]
shiori grep <query> --allow <directory> [--limit <count>]
shiori doctor
```

`find` and `grep` never read outside the canonical `--allow` workspace.
