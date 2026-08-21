# Shiori v1.2.0

Shiori v1.2.0 improves unified search ordering with bounded Git-aware ranking.

## Changes

- Favors Git-tracked files when their search relevance is otherwise similar.
- Applies a small, time-bounded boost to recently changed tracked files.
- Preserves the existing ranking when Git is unavailable, the workspace is not
  a repository, metadata collection times out, or a candidate path is unsafe.
- Keeps the public search response and configuration format backward compatible.
