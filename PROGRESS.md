# Shiori Progress

更新日: 2026-08-16

## 完成度

- v1全体: **81%**（仕様書のv1完成条件21項目中17項目完了）
- 現在のフェーズ: **Phase 2 — 100%**（5項目中5項目完了）

## Phase 2

- [x] Tree-sitter
- [x] Symbol extraction
- [x] SQLite FTS5
- [x] Incremental index
- [x] File watcher

## v1完了済み

MCP Streamable HTTP server、Workspace isolation、SQLite database、File index、
ripgrep search、Tree-sitter parsing、Symbol index、SQLite FTS5、Incremental indexing、File watcher、File search、
Text search、Symbol search、File outline、Index status、CLI、Windows support。

## v1残作業（優先順）

1. Generic search tool、Query Planner、統合Ranking
2. Everything optional integration
3. Claude Code integration
4. Codex integration

## 算定基準

全体完成度は仕様書「v1必須機能」の21項目を等重みで算出する。
現在フェーズは仕様書「開発優先順位」の当該Phase内項目を等重みで算出する。
部分実装は完了へ算入せず、外部仕様・実装・検証が揃った時点で完了とする。
