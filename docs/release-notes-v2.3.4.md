# Shiori v2.3.4

Shiori v2.3.4 makes successful index completion explicit in Windows Terminal.

## Fixed

- Prints a localized completion message with the workspace path and indexed
  file count after the index is successfully published.
- Keeps failure output unambiguous by omitting the completion message when
  indexing or publication fails.

Workspace registration and indexing never modify or delete source files.
