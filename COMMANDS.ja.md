# Shioriコマンド

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

Shiori CLIの詳細リファレンスです。成功した検索・管理コマンドはJSONを標準出力へ、
エラーは標準エラーへ出力します。

## コマンドグループ

| グループ | コマンド | 説明 |
| :--- | :--- | :--- |
| [検索](#検索コマンド) | `find`, `grep`, `search`, `symbol`, `ast`, `outline`, `navigate` | ファイルやコードの検索・調査 |
| [インデックス](#インデックスコマンド) | `index build`, `index status`, `index rebuild` | ワークスペース単位のインデックス管理 |
| [ワークスペース](#ワークスペースコマンド) | `workspace add`, `workspace list`, `workspace remove` | CLI登録の管理 |
| [連携・運用](#連携・運用コマンド) | `config claude`, `config codex`, `serve`, `doctor` | MCPの設定・運用 |

## 共通オプション

- `--allow <directory>`: 存在する絶対ワークスペースルート。直接検索、アウトライン、
  ナビゲーション、インデックスコマンドで必須です。
- `--path <path>`: 任意のワークスペース相対パスフィルターです。
- `--limit <1-100>`: 最大結果数です。既定値は`20`です。
- 終了コード`0`は成功です。`1`は不正入力、実行時依存関係の不足、処理失敗、
  または必須診断の異常を表します。

## 検索コマンド

コマンド: [`find`](#find)、[`grep`](#grep)、[`search`](#search)、
[`symbol`](#symbol)、[`ast`](#ast)、[`outline`](#outline)、
[`navigate`](#navigate)

### `find`

目的: インデックス済みファイル名・パス検索。構文: `shiori find <query> --allow
<directory> [--limit <1-100>]`。`query`は空にできません。例:
`shiori find README --allow F:\Projects\Shiori --limit 10`。ワークスペースDBを
開き、`{"results":[{"type":"file","path":"README.md"}]}`形式で返します。
`--allow`外は読みません。

### `grep`

目的: ripgrepによるテキスト検索。構文: `shiori grep <query> --allow <directory>
[--path <path>] [--glob <glob>] [--regex] [--case-sensitive] [--context <0-10>]
[--limit <1-100>]`。既定はリテラル・大文字小文字非区別です。例:
`shiori grep TODO --allow F:\Projects\Shiori --glob *.md`。結果には相対パス、
1始まりの行・列、スニペットが含まれます。結果なしは`{"results":[]}`です。
正規表現は`--regex`指定時だけ解釈します。

### `search`

目的: ファイル、シンボル、テキストプロバイダーの計画検索。構文:
`shiori search <query> --allow <directory> [--path <path>] [--limit <1-100>]`。
例: `shiori search WorkspaceRegistry --allow F:\Projects\Shiori`。Query Plannerが
プロバイダーを選択し、位置を順位付け・重複排除します。結果、選択プロバイダー、
回復可能なエラーをJSONで返し、一部エラーと成功結果が共存する場合があります。

### `symbol`

目的: インデックス済みシンボル検索。構文: `shiori symbol <query> --allow
<directory> [--kind <kind>] [--language <language>] [--path <path>]
[--limit <1-100>]`。例: `shiori symbol RunServer --language csharp --allow
F:\Projects\Shiori`。完全修飾名、種類、言語、パス、1始まりの位置を返します。
フィルターはメタデータの完全一致です。

### `ast`

目的: Tree-sitter構造検索。構文: `shiori ast <tree-sitter-query> --language
<language> --allow <directory> [--path <path>] [--limit <1-100>]`。対応言語は
`c`, `cpp`, `csharp`, `go`, `java`, `javascript`, `python`, `rust`, `typescript`です。
例: `shiori ast '(class_declaration name: (identifier) @name)' --language csharp
--allow F:\Projects\Shiori`。キャプチャ名、ノード種類、パス、位置、スニペットを
返します。不正なクエリは終了コード`1`です。

### `outline`

目的: 1ソースファイルのインデックス済みシンボルを返します。構文:
`shiori outline <source-file> --allow <directory>`。ファイルは絶対パスまたは
ワークスペース相対パスで、ワークスペース内に限ります。例: `shiori outline
src\Shiori.Cli\Program.cs --allow F:\Projects\Shiori`。言語と順序付きシンボル
ツリーを返し、非対応ファイルでは空のアウトラインを返します。

### `navigate`

目的: 外部言語サーバーによるC#セマンティックナビゲーション。構文:
`shiori navigate <definition|references|implementations|callers|callees> <file>
--line <one-based> --column <one-based> --allow <directory> [--limit <1-100>]`。
例: `shiori navigate definition src\Shiori.Cli\Program.cs --line 20 --column 18
--allow F:\Projects\Shiori`。`success`、位置、失敗時のエラーを返します。
`csharp-ls`またはOmniSharpが必要で、座標は1始まりです。

## インデックスコマンド

コマンド: [`index build`](#index-build)、[`index status`](#index-status)、
[`index rebuild`](#index-rebuild)

### `index build`

目的: 1ワークスペースのインデックス作成または差分更新。構文:
`shiori index build --allow <directory>`。例: `shiori index build --allow
F:\Projects\Shiori`。既存メタデータとハッシュを使って不要な解析を避け、追加・
変更・削除ファイルだけをSQLiteへ反映します。ID、状態、ファイル・シンボル数、
バージョン、走査日時を返します。

### `index status`

目的: 再構築せず永続インデックスを確認します。構文: `shiori index status
--allow <directory>`。例: `shiori index status --allow F:\Projects\Shiori`。
`index build`と同じ状態スキーマを返し、未作成時は未構築状態と件数0を返します。

### `index rebuild`

目的: 完全再走査を強制します。構文: `shiori index rebuild --allow <directory>`。
例: `shiori index rebuild --allow F:\Projects\Shiori`。全ファイル・シンボル行を
更新して状態を返します。`index build`より高コストなため、復旧またはパーサー
変更時に使用します。

## ワークスペースコマンド

コマンド: [`workspace add`](#workspace-add)、[`workspace list`](#workspace-list)、
[`workspace remove`](#workspace-remove)

### `workspace add`

目的: CLI検索用ワークスペース登録とDB初期化。構文: `shiori workspace add
<absolute-directory>`。例: `shiori workspace add F:\Projects\Shiori`。ID、名前、
正規化パスを返します。同一IDは更新し、同名競合は拒否します。MCPアクセスは
許可しません。

### `workspace list`

目的: 登録一覧。構文・例: `shiori workspace list`。名前・パスの安定順で
`{"workspaces":[...]}`を返します。未登録時は空配列と終了コード`0`です。

### `workspace remove`

目的: 名前、ID、絶対パスによる登録解除。構文: `shiori workspace remove
<identifier>`。例: `shiori workspace remove Shiori`。解除した登録を返し、SQLite
DBは保持します。不明または曖昧な識別子は安全に失敗します。

## 連携・運用コマンド

コマンド: [`config claude`](#config-claude)、[`config codex`](#config-codex)、
[`serve`](#serve)、[`doctor`](#doctor)

### `config claude`

目的: Claude Code用プロジェクトMCP JSON生成。構文: `shiori config claude
[--port <1-65535>] [--name <server-name>]`。既定値は`39473`と`shiori`です。
例: `shiori config claude > .mcp.json`。Streamable HTTPと
`${SHIORI_MCP_TOKEN}`を出力し、トークン値は書きません。サーバー名には英数字、
`_`、`-`だけを使用できます。

### `config codex`

目的: Codex用MCP TOMLセクション生成。構文: `shiori config codex
[--port <1-65535>] [--name <server-name>]`。例: `shiori config codex`の出力を
`%USERPROFILE%\.codex\config.toml`へ統合します。
`bearer_token_env_var = "SHIORI_MCP_TOKEN"`を設定し、値は書きません。

### `serve`

目的: 単一のステートレスStreamable HTTP MCPサーバーを実行します。構文:
`shiori serve [--port <1-65535>]`。既定ポートは`39473`です。例:
`shiori serve --port 39473`。ループバックだけにバインドし、`/health`と認証済み
`/mcp`を公開して停止まで動作します。`SHIORI_MCP_TOKEN`と
`SHIORI_ALLOWED_WORKSPACES`が必須で、起動失敗は終了コード`1`です。

### `doctor`

目的: Native ABI、SQLite/FTS5、ripgrep、Tree-sitter、任意のC# LSP、データ
ディレクトリアクセス、MCP環境設定を診断します。構文・例: `shiori doctor`。
`{"status":"ok|warning|error","checks":[...]}`を返します。任意LSP不足などの
警告は`0`、必須ランタイム異常は`1`を返します。

## 安全上の注意

全ファイル操作に正規ワークスペース検査を適用します。MCP認可は
`SHIORI_ALLOWED_WORKSPACES`だけで制御し、CLI登録はアクセスを許可しません。
`index rebuild`は意図的な完全再走査を行う唯一のコマンドですが、ソースファイルを
削除しません。
