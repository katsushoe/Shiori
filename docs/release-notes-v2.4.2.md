# Shiori v2.4.2

Shiori v2.4.2 adds file-name prefix and suffix search.

## Added

- MCP `search_files` accepts `nameStartsWith` and `nameEndsWith`; `shiori find`
  accepts `--name-starts-with` and `--name-ends-with`. They match the start and
  end of the file name, can be combined with the path fragment `query`, and
  every supplied condition must match. `query` is optional when a name condition
  is given.
- Matching is case-insensitive for ASCII letters, and `%` and `_` are treated
  literally.

## Changed

- The native engine ABI is now version 7. Existing indexes remain valid.
