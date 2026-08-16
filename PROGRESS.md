# Shiori Progress

更新日: 2026-08-16

## 完成度

- v1全体: **100%**（仕様書のv1完成条件20項目中20項目完了）
- 現在のフェーズ: **v1 Release Preparation — 100%**（8項目中8項目完了）
- 対象範囲内のv1.1全体: **100%**（macOS・Linux正式対応を除く5項目中5項目完了）
- 現在のフェーズ: **Windows向けv1.1 — 100%**

## Multi-workspace coordination（完了）

- [x] Workspace別SQLite DBと遅延常駐Engineを維持
- [x] MCPサーバでWorkspace別Taskへbounded fan-out
- [x] `search_files`の複数・全Workspace検索と結果統合
- [x] 同期完了型`update_indexes`とWorkspace別更新直列化
- [x] 部分失敗をWorkspace別エラーとして分離
- [x] ADR・仕様書・README・単体テスト・実MCP検証

## Phase 2（完了）

- [x] Tree-sitter
- [x] Symbol extraction
- [x] SQLite FTS5
- [x] Incremental index
- [x] File watcher

## Phase 3（完了）

- [x] Query Planner
- [x] Unified search
- [x] Ranking

## v1 Client Integration

- [x] Claude Code integration
- [x] Codex integration

Claude Code 2.1.233でStreamable HTTP接続と`search` Tool実行を確認済み。
Codex CLI 0.147.0でStreamable HTTP接続と`search` Tool実行を確認済み。

## v1完了済み

MCP Streamable HTTP server、Workspace isolation、SQLite database、File index、
ripgrep search、Tree-sitter parsing、Symbol index、SQLite FTS5、Incremental indexing、File watcher、File search、
Text search、Symbol search、File outline、Index status、CLI、Windows support。

## v1残作業（優先順）

なし。

## v1リリース準備

進捗と残作業は [`RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md) を参照。

## 算定基準

全体完成度は仕様書「v1必須機能」の20項目を等重みで算出する。
現在フェーズは仕様書「開発優先順位」の当該Phase内項目を等重みで算出する。
部分実装は完了へ算入せず、外部仕様・実装・検証が揃った時点で完了とする。
