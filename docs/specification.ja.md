# SPEC.md Version
2026.08.17

> **v1.x仕様（置換予定）:** v2.0.0の実装仕様は
> [`specification-v2.0.ja.md`](specification-v2.0.ja.md)を正本とします。

# 変更履歴

- 2026.08.16
- 2026.08.17

# Shiori 仕様書

**開発コード名:** Shiori
**種別:** File Search MCP Server（Code Search / Code Navigation対応）
**対象:** Claude Code / OpenAI Codex / その他MCP対応AI Coding Agent
**ライセンス:** MIT
**実装言語:** C#（Managed Host）およびRust（Native Engine）
**永続ストレージ:** SQLite
**基本方針:** ローカル完結・高速・読み取り専用

---

## 1. 概要

Shioriは、Claude CodeやCodexなどのAI Agentに対して、許可されたローカルworkspace内の
ファイル名とパスを高速に検索するMCPサーバである。コード検索・解析・ナビゲーションは、
発見したファイルを詳しく調べるための補助機能として提供する。

通常のAI Coding Agentは、ファイル探索、grep、ファイル読み込みを繰り返しながら目的のコードへ到達する。

Shioriでは、

* ripgrep
* SQLite
* Tree-sitter
* LSP

を組み合わせ、検索内容に最適な検索エンジンを自動選択する。

```text
Claude Code / Codex
        │
        │ MCP
        ▼
      Shiori
        │
        ├── File Search ───── SQLite
        ├── Text Search ───── ripgrep
        ├── Symbol Search ─── Tree-sitter + SQLite FTS5
        ├── AST Search ────── Tree-sitter
        └── Navigation ────── LSP
```

目的は単純に`grep`をMCP化することではない。

**「AIが目的のファイルへ到達するまでに必要な検索回数・時間・トークン数を減らす」**

ことをShioriの主要目的とする。

---

$2## 2. 名前

$2### 2.1 開発コード名

**Shiori**

日本語の「栞」に由来する。

巨大なコードベースの中から目的の場所を素早く見つけ、必要なコードへ直接移動する役割を表す。

英語説明例：

> Fast local-first file search server for AI agents, with indexed code search and navigation.

GitHub Description案：

> Fast local-first file search server for Claude Code, Codex, and other AI agents, with indexed code search and navigation capabilities.

---

$2## 3. 設計目標

Shioriは以下を最優先する。

### 高速

検索要求ごとに全ファイルを走査することを可能な限り避ける。

ファイル、シンボル、依存関係など検索可能な情報については事前インデックスを利用する。

### ローカル完結

コード、シンボル、インデックス、検索クエリを外部サーバへ送信しない。

Shiori本体には外部AI APIへの通信機能を実装しない。

### AI向け

人間向け検索UIではなく、

```text
Claude Code
Codex
AI Agent
```

が利用することを第一に設計する。

検索結果は大量のテキストではなく、

```text
path
line
symbol
type
score
snippet
```

など構造化された情報として返す。

### 低トークン消費

検索結果を必要以上に返さない。

デフォルトでは関連度の高い結果だけを返し、AIが必要に応じて検索範囲を拡張する。

### 即時利用可能

初回インデックス作成が完了していなくても検索可能とする。

Shiori起動直後はripgrepなどを使用して検索し、インデックス作成完了後は高速インデックス検索へ自動的に切り替える。

### 読み取り専用

Shioriは読み取り専用のFile Searchを主機能とし、Code Search / Code Navigationを補助機能とする。

ソースコード変更機能は持たない。

---

$2## 4. 非目標

v1では以下を対象外とする。

* コード編集
* Git操作
* GitHub操作
* タスク管理
* Agent間メッセージング
* ビルド実行
* テスト実行
* コマンド実行代行
* AI推論
* Embedding生成
* Vector DB
* クラウドコードインデックス
* ソースコードの外部送信

Shioriはあくまで

**「高速なローカルファイル探索レイヤー」**

として設計する。

---

$2## 5. 全体アーキテクチャ

```text
┌────────────────────────────────────────┐
│ Claude Code / Codex / MCP Client       │
└───────────────────┬────────────────────┘
                    │ MCP
                    ▼
┌────────────────────────────────────────┐
│ Shiori MCP Server                      │
│                                        │
│  MCP Tool Layer                        │
│          │                             │
│          ▼                             │
│  Query Planner                         │
│          │                             │
│   ┌──────┼────────┬────────┬───────┐   │
│   ▼      ▼        ▼        ▼       ▼   │
│ File   Text     Symbol    AST     LSP  │
│ Search Search   Search   Search   Nav  │
│   │      │        │        │       │   │
│   ▼      ▼        ▼        ▼       ▼   │
│ SQLite  rg       SQLite  Tree-   LSP   │
│                   FTS5   sitter Server │
│                                        │
│          Index Manager                 │
│               │                        │
│               ▼                        │
│           SQLite DB                    │
└────────────────────────────────────────┘
                    │
                    ▼
             Local Workspace
```

---

$2## 6. 主要コンポーネント

$2### 6.1 MCP Server

Claude Code / Codexとの通信を担当する。

v1必須Transport：

```text
Streamable HTTP
```

単一のローカル常駐サーバとして起動し、複数のMCP Clientでインデックス、
File Watcher、Tree-sitter、LSP、キャッシュを共有する。

stdioはクライアント互換性が必要な場合の将来Adapter候補とする。

---

$2## 7. Query Planner

Shioriの中核コンポーネント。

ユーザーまたはAI Agentから渡された検索要求を解析し、最適な検索エンジンを決定する。

例：

```text
"AccountDTO.cs"
```

→ File Search

```text
"SaveAccount"
```

→ Symbol Search + Text Search

```text
"SaveAccountを呼び出している場所"
```

→ LSP References

```text
"UPDATE accounts"
```

→ ripgrep

```text
"AccountDTOを継承しているクラス"
```

→ LSP / Symbol Graph

となる。

Query Plannerは必要に応じて複数エンジンを並列実行する。

---

$2## 8. File Search Engine

ファイル名・パス検索を担当する。

## Windows / Linux / macOS

```text
SQLite File Index
    ↓
filesystem walk
```

SQLite File Indexを標準とする。

必要に応じてOS固有の高速検索エンジンを将来追加可能にする。

---

$2## 9. Text Search Engine

全文検索には原則として

**ripgrep**

を使用する。

対象：

* 文字列検索
* 正規表現検索
* TODO検索
* SQL検索
* コメント検索
* エラーメッセージ検索
* 任意コード断片検索

例：

```text
search_text(
    query = "UPDATE accounts",
    glob = "*.cs"
)
```

Shiori内部でripgrepを起動し、出力をMCP向け構造に変換する。

---

$2## 10. Symbol Index Engine

クラス、メソッド、関数、フィールド、interfaceなどを検索する。

解析には

**Tree-sitter**

を使用する。

取得対象：

```text
class
struct
interface
enum
function
method
property
field
constant
namespace
module
trait
type
constructor
```

言語に応じてTree-sitter grammarを使用する。

---

$2## 11. Symbol Search

Tree-sitterで取得したシンボルはSQLiteに保存する。

検索にはSQLite FTS5を利用する。

例：

```text
SaveAccount
AccountDTO
SmtpAccountPanel
CmFolderTree
```

に対して毎回ソースコード全体をgrepしない。

```text
Agent
  │
  ▼
Shiori
  │
  ▼
SQLite FTS5
  │
  ▼
symbols
```

という検索を行う。

---

$2## 12. LSP Engine

意味的なコードナビゲーションにはLanguage Server Protocolを利用する。

対応機能：

```text
definition
references
implementation
typeDefinition
callHierarchy
hover
documentSymbol
workspaceSymbol
```

代表的用途：

```text
このメソッドの定義は？
このinterfaceの実装は？
このクラスを使っている場所は？
このメソッドを呼び出している場所は？
```

これらを単純な文字列検索ではなくLSPによって解決する。

---

$2## 13. LSPはLazy Startとする

すべてのworkspaceで常時Language Serverを立ち上げない。

必要になった時点で起動する。

```text
search_files
search_text
search_symbols
```

だけならLSPは起動しない。

```text
find_references
find_definition
find_implementations
```

などが要求された場合のみ起動する。

これによりShioriの起動を高速化する。

---

$2## 14. AST Search

Tree-sitterを利用してコード構造検索を行う。

例えば、

```text
すべてのasyncメソッド
特定interfaceを実装したclass
特定API呼び出し
特定属性を持つmethod
```

などを検索可能とする。

将来的にはast-grep互換パターンの導入も検討する。

---

$2## 15. SQLite

SQLiteをShioriの唯一の永続インデックスストレージとする。

インデックス情報をMarkdownファイル等へ保存しない。

DB例：

```text
%LOCALAPPDATA%\Shiori\
    indexes\
        <workspace-id>\
            shiori.db
```

Linux：

```text
~/.local/share/shiori/
```

macOS：

```text
~/Library/Application Support/Shiori/
```

---

$2## 16. SQLite設定

基本設定：

```sql
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
PRAGMA temp_store=MEMORY;
```

読み取り処理とインデックス更新処理を可能な限り競合させない。

---

$2## 17. データベース構造

主要テーブル：

### workspaces

```text
id
path
name
created_at
updated_at
last_indexed_at
```

### files

```text
id
workspace_id
path
relative_path
extension
language
size
mtime
content_hash
indexed_at
```

INDEX：

```text
workspace_id
relative_path
extension
language
mtime
```

---

### symbols

```text
id
workspace_id
file_id
name
qualified_name
kind
language
start_line
start_column
end_line
end_column
parent_symbol_id
signature
```

---

### symbols_fts

SQLite FTS5仮想テーブル。

検索対象：

```text
name
qualified_name
signature
```

---

### references

```text
id
workspace_id
file_id
symbol_id
target_name
reference_kind
line
column
```

Tree-sitterだけで正確に解決できない参照についてはNULLを許容する。

---

### dependencies

```text
id
workspace_id
source_file_id
target
dependency_type
line
```

対象：

```text
import
using
require
include
package
module
```

---

### index_state

```text
workspace_id
index_version
parser_version
last_scan
last_full_index
status
```

---

$2## 18. ソースコード本文

v1ではソースコード全文をSQLiteへ保存しない。

理由：

* DB肥大化
* 書き込み負荷
* ripgrepとの機能重複
* workspace自体が原本であるため

SQLiteには、

```text
path
mtime
hash
symbols
references
dependencies
```

など検索高速化に必要なメタデータのみ保存する。

全文検索はripgrepを使用する。

---

$2## 19. Incremental Index

毎回フルインデックスを作成しない。

ファイルごとに、

```text
mtime
size
hash
```

を確認する。

変更されたファイルだけTree-sitterで再解析する。

処理：

```text
File change
    │
    ▼
File Watcher
    │
    ▼
Change Queue
    │
    ▼
Tree-sitter Parse
    │
    ▼
SQLite Transaction
```

---

$2## 20. File Watcher

workspaceを監視する。

監視対象：

```text
create
modify
rename
delete
```

大量変更時にはイベントをdebounceする。

例：

```text
git checkout
git pull
npm install
dotnet restore
```

などで大量イベントが発生しても1ファイルずつ即時解析しない。

---

$2## 21. 除外ルール

デフォルト除外：

```text
.git
node_modules
bin
obj
target
dist
build
.vs
.idea
.next
coverage
vendor
packages
```

`.gitignore`を尊重する。

追加除外設定も可能とする。

---

$2## 22. MCP Tools

v1ではTool数を過剰に増やさない。

AI AgentがTool選択に迷わない構造とする。

## `search`

コード内容やシンボルを調べるための補助検索Tool。

Query Plannerが検索方式を自動決定する。

入力例：

```json
{
  "query": "SaveAccount",
  "workspace": "CupperMail",
  "limit": 20
}
```

内部では必要に応じて、

```text
symbol
file
text
```

を並列検索する。

---

## `search_files`

Shioriの主機能であり、AIがファイルを探すときに最初に使用する推奨Tool。
ファイル名・パスを検索し、`workspace`、`workspaces`のいずれも省略した場合は、
許可済み全workspaceを検索する。複数workspaceはMCPサーバ側でTaskへfan-outし、
workspace識別情報を付けて統合・ランキングする。

成功・失敗にかかわらず、workspaceごとに検索対象workspace名、SQLiteの公開済み
インデックスから取得した検索対象ディレクトリ数・ファイル数、検索結果（OK／NG）、
返却ヒット数、インデックスステータスを構造化して返し、同じ内容のMarkdown表を
利用者へ表示する。検索開始から結果の統合・順位付け・要約生成までの所要時間を
ミリ秒単位の`elapsedMilliseconds`として返す。MCP通信とAI側の待機時間は含めない。
実ディレクトリを件数集計のために再走査しない。

公開済みインデックスがない場合、`search_files`は読み取り専用のままNGと確認要求を返す。
`not_indexed`ではインデックス作成、`indexing`ではインデックス再開について利用者へ
確認し、同意後にだけ`index_build`を呼び出す。公開済みの旧世代がある場合は、作成中でも
旧世代を検索対象とする。

入力：

```text
query
workspace（任意、単一workspace互換）
workspaces（任意、複数workspace）
limit
```

---

## `search_text`

ripgrep全文検索。

入力：

```text
query
workspace
path
glob
regex
case_sensitive
context_lines
limit
```

---

## `search_symbols`

シンボル検索。

入力：

```text
query
workspace
kind
language
path
limit
```

---

## `navigate`

シンボルナビゲーション。

action：

```text
definition
references
implementations
type_definition
callers
callees
```

入力：

```text
file
line
column
action
limit
```

---

## `search_ast`

AST構造検索。

入力：

```text
language
pattern
workspace
path
limit
```

---

## `file_outline`

ファイル内の構造を取得する。

出力例：

```text
namespace CupperMail

class AccountService
 ├─ SaveAccount()
 ├─ UpdateAccount()
 └─ DeleteAccount()
```

AIがファイル全文を読む前に構造を把握できる。

---

## `index_status`

workspaceのインデックス状態を取得する。

---

## `reindex`

指定workspaceまたはpathを再インデックスする。

```text
workspace
path
force
```

---

## `update_indexes`

ユーザーが「検索DBを更新する」と依頼したとき、指定または許可済み全workspaceの
検索DBを更新する。既定は差分更新とし、すべての対象が完了してから応答する。

```text
workspaces（省略時は許可済み全workspace）
force（trueの場合は全再構築）
```

異なるworkspaceは並列更新し、同一workspaceへの更新要求は直列化する。
一部のworkspaceが失敗しても、成功結果とworkspace別エラーを構造化して返す。

---

$2## 23. 検索結果フォーマット

結果は原則として構造化する。

例：

```json
{
  "results": [
    {
      "type": "symbol",
      "name": "SaveAccount",
      "kind": "method",
      "path": "CupperMail/AccountService.cs",
      "line": 142,
      "column": 18,
      "score": 0.98,
      "snippet": "public async Task SaveAccount(...)"
    }
  ]
}
```

---

$2## 24. 検索結果サイズ

AIのコンテキストを浪費しないことを重要要件とする。

デフォルト：

```text
limit = 20
```

最大：

```text
limit = 100
```

snippetは前後数行に限定する。

大量結果をそのままMCPレスポンスへ入れない。

---

$2## 25. Ranking

複数検索エンジンから結果を取得した場合は統合ランキングする。

優先例：

```text
Exact symbol match
Prefix symbol match
Exact filename match
Qualified symbol match
Path match
Text match
Fuzzy symbol match
```

同一コード位置が複数エンジンから返された場合は重複排除する。

---

$2## 26. Search Planner例

Agent：

```text
AccountDTOはどこ？
```

Shiori：

```text
search_symbols("AccountDTO")
search_files("*AccountDTO*")
```

を並列実行。

---

Agent：

```text
"Application-specific password required"を出している場所
```

Shiori：

```text
search_text(...)
```

---

Agent：

```text
SaveAccountを呼んでいる場所
```

Shiori：

```text
navigate(action="references")
```

LSPが利用できない場合：

```text
symbol index
+
ripgrep
```

へフォールバックする。

---

$2## 27. Fallback設計

Shioriは一部外部ツールがなくても動作する。

```text
ripgrep unavailable
    → internal filesystem search

LSP unavailable
    → Tree-sitter + Text Search

Tree-sitter language unsupported
    → Text Search
```

機能が完全停止するのではなく、検索精度または速度を下げて継続する。

---

$2## 28. Workspace

複数workspaceを登録可能とする。

例：

```text
F:\Projects\Cupper
F:\Projects\Hataori
F:\Projects\Itoguruma
F:\Projects\Shiori
```

親ディレクトリ：

```text
F:\Projects
```

を1つのworkspaceとして登録することも可能とする。

各workspaceは独立したSQLite DBと遅延生成されるNative Engineを持つ。
複数workspace検索では共有DBへ統合せず、MCPサーバのCoordinatorがThreadPool上の
Taskとして各Engineを並列実行し、結果をfan-inする。専用AI Agentやworkspace数分の
常駐OSスレッドは生成しない。詳細はADR 0002を正本とする。

---

$2## 29. Workspace ID

絶対パスを正規化した値からstable IDを生成する。

例：

```text
SHA256(normalized_absolute_path)
```

workspace移動時は別workspaceとして扱う。

---

$2## 30. セキュリティ

Shioriは明示されたworkspace外を検索しない。

MCP Server起動時に、

```text
--allow F:\Projects
```

を指定可能とする。

複数指定：

```text
--allow F:\Projects
--allow C:\Source
```

canonical pathを検証し、

```text
..
symlink
junction
```

などによるworkspace外への脱出を防止する。

---

$2## 31. ネットワーク

Shiori Coreは外部ネットワーク通信を行わない。

検索データ、コード、インデックスを外部送信しない。

v1ではTelemetryもデフォルトOFFとする。

---

$2## 32. CLI

実行ファイル：

```text
shiori
```

---

## MCP Server起動

```bash
shiori serve --port 39473
```

起動前にBearer tokenと許可workspaceを環境変数で指定する：

```powershell
$env:SHIORI_MCP_TOKEN = "<32文字以上のランダム値>"
$env:SHIORI_ALLOWED_WORKSPACES = "F:\Projects\Cupper;F:\Projects\Shiori"
```

---

## Workspace追加

```bash
shiori workspace add F:\Projects\Cupper
```

---

## Workspace一覧

```bash
shiori workspace list
```

---

## Workspace削除

```bash
shiori workspace remove Cupper
```

---

## インデックス作成

```bash
shiori index build
```

workspace指定：

```bash
shiori index build Cupper
```

---

## 状態確認

```bash
shiori index status
```

---

## インデックス再構築

```bash
shiori index rebuild Cupper
```

---

## File Search

```bash
shiori find AccountDTO
```

---

## Text Search

```bash
shiori grep "UPDATE accounts"
```

---

## Symbol Search

```bash
shiori symbol SaveAccount
```

---

## References

```bash
shiori refs SaveAccount
```

---

## 診断

```bash
shiori doctor
```

確認項目：

```text
SQLite
ripgrep
Tree-sitter parsers
LSP servers
workspace permissions
index DB
```

---

$2## 33. MCP設定生成

Claude Code向け設定を生成可能にする。

```bash
shiori config claude
```

標準出力はClaude Codeのproject scope `.mcp.json`形式とし、Bearer tokenは
`${SHIORI_MCP_TOKEN}` の環境変数参照として出力する。秘密値は設定へ埋め込まない。

Codex：

```bash
shiori config codex
```

将来的には、

```bash
shiori install claude
shiori install codex
```

で自動登録する機能も検討する。

---

$2## 34. 設定ファイル

任意で、

```text
shiori.toml
```

を使用可能とする。

例：

```toml
[server]
transport = "streamable_http"
host = "127.0.0.1"
port = 39473

[search]
default_limit = 20
max_limit = 100

[index]
watch = true
gitignore = true

[ripgrep]
enabled = true

[lsp]
enabled = true
lazy_start = true

[[workspace]]
path = "F:\\Projects"
```

ただしインデックス状態など動的データは設定ファイルへ保存しない。

永続状態の正本はSQLiteとする。

---

$2## 35. File Search Provider

Provider interface：

```text
FileSearchProvider
```

実装：

```text
SQLiteProvider
FilesystemProvider
```

SQLiteを標準providerとし、利用不能時はFilesystemProviderへフォールバックする。

---

$2## 36. Language Adapter

プログラミング言語ごとの差異をAdapterとして分離する。

```text
LanguageAdapter
 ├─ CSharpAdapter
 ├─ TypeScriptAdapter
 ├─ JavaScriptAdapter
 ├─ PythonAdapter
 ├─ RustAdapter
 ├─ GoAdapter
 ├─ JavaAdapter
 └─ ...
```

Adapterは、

```text
Tree-sitter grammar
symbol extraction
import extraction
LSP configuration
```

を提供する。

---

$2## 37. 初期対応言語

v1で優先する言語：

```text
C#
TypeScript
JavaScript
Python
Rust
Go
Java
C
C++
```

特にC#を初期の重点対応言語とする。

---

$2## 38. C#対応

C#では、

```text
class
interface
record
struct
enum
method
property
field
namespace
using
```

をTree-sitterから取得する。

LSPについては利用可能なC# Language Serverを自動検出可能な設計とする。

---

$2## 39. インデックス作成フロー

```text
Workspace Registration
        │
        ▼
File Enumeration
        │
        ▼
.gitignore / exclusion filtering
        │
        ▼
File metadata → SQLite
        │
        ▼
Tree-sitter parsing
        │
        ├── Symbols
        ├── Imports
        └── Structure
        │
        ▼
SQLite transaction
        │
        ▼
FTS5 index
```

インデックス中でもMCP検索を停止しない。

---

$2## 40. Startup

Shiori起動時にフルスキャンを必須にしない。

起動：

```text
open SQLite
     ↓
validate index
     ↓
start MCP
     ↓
accept queries
     ↓
incremental validation
```

Agentからの検索受付を最優先する。

---

$2## 41. Git操作への対応

以下の場合、大量ファイル変更が発生する。

```text
git checkout
git switch
git reset
git pull
```

File Watcherイベントをdebounceし、

一定時間内の変更をまとめてインデックス更新する。

---

$2## 42. Performance目標

性能は環境依存だが、v1の目標値を設定する。

Warm状態：

```text
File search:
50 ms以下を目標

Symbol exact search:
100 ms以下

Symbol fuzzy search:
200 ms以下

Indexed outline:
100 ms以下
```

Text Search：

```text
1M LOC:
300 ms前後を目標

10M LOC:
1秒以内を目標
```

LSP検索：

Language Server依存のため明確な保証対象外とする。

重要なKPIは単体の検索時間だけではなく、

**Agentが目的のコードへ到達するまでの総時間**

とする。

---

$2## 43. Benchmark

以下を自動測定する。

```text
cold startup
warm startup
file search
text search
symbol exact
symbol fuzzy
references
incremental index
full index
memory usage
DB size
```

リポジトリ規模：

```text
1,000 files
10,000 files
100,000 files
```

で比較する。

---

$2## 44. Observability

標準ログレベル：

```text
error
warn
info
debug
trace
```

Streamable HTTP MCPでは構造化ログを標準ログ出力へ出力する。
検索文字列やソースコード本文、Bearer tokenはログへ出力しない。

---

$2## 45. Search Metrics

debug時には、

```text
engine
duration_ms
results
cache_hit
workspace
files_scanned
```

を確認可能にする。

ソースコード本文や検索文字列をログへ残す機能はデフォルトOFFとする。

---

$2## 46. エラー設計

例えばLSPが起動できない場合：

```json
{
  "success": false,
  "code": "LSP_UNAVAILABLE",
  "message": "C# language server is unavailable.",
  "fallback_available": true
}
```

Agentが次の行動を判断できる構造化エラーとする。

---

$2## 47. キャッシュ

キャッシュ対象：

```text
File metadata
Symbols
AST-derived metadata
Dependencies
Search ranking metadata
```

キャッシュキー：

```text
workspace
file
mtime
hash
parser_version
```

Parser versionが変わった場合は対象インデックスのみ無効化する。

---

$2## 48. Concurrent Search

独立した検索は並列実行する。

例：

```text
search("AccountDTO")
```

内部：

```text
┌─ Symbol Search
├─ File Search
└─ Text Search
```

を並列実行し、最後に統合ランキングする。

複数workspaceが選択された場合は、検索Provider内でもworkspaceごとの独立Taskへ
fan-outする。同時実行数はCPUとストレージを枯渇させないよう上限を設ける。
結果にはworkspace ID、名前、ルートパスを含め、同じ相対パスを区別する。

---

$2## 49. Cancellation

MCP Clientが検索をキャンセルした場合、可能な限り検索処理を中断する。

ripgrep child processもterminateする。

巨大検索が不要になった後もバックグラウンドで走り続けないようにする。

---

$2## 50. Repository構成案

```text
shiori/
├─ Cargo.toml
├─ README.md
├─ LICENSE
├─ crates/
│  ├─ shiori-core/
│  │  ├─ search/
│  │  ├─ index/
│  │  ├─ workspace/
│  │  └─ ranking/
│  │
│  ├─ shiori-db/
│  │  ├─ sqlite/
│  │  └─ migration/
│  │
│  ├─ shiori-tree-sitter/
│  │  └─ languages/
│  │
│  ├─ shiori-lsp/
│  │
│  ├─ shiori-ripgrep/
│  │
│  ├─ shiori-mcp/
│  │
│  └─ shiori-cli/
│
├─ tests/
├─ benchmarks/
└─ docs/
```

---

$2## 51. 推奨技術スタック

Core：

```text
Rust
Tokio
```

MCP：

```text
Rust MCP SDK
```

DB：

```text
SQLite
FTS5
```

Parsing：

```text
Tree-sitter
```

全文検索：

```text
ripgrep
```

ファイル検索：

```text
SQLite
```

Semantic Navigation：

```text
LSP
```

File Watch：

```text
OS native watcher abstraction
```

---

$2## 52. Rustを推奨する理由

ShioriはAIアプリではなく、

**ローカル検索インフラ**

である。

そのため、

```text
高速起動
低メモリ
並列検索
単一バイナリ配布
Windows / Linux / macOS
Tree-sitter連携
SQLite連携
```

との相性を重視し、Rustを第一候補とする。

ただし仕様自体は実装言語へ強く依存させない。

---

$2## 53. Claude Code / Codexから見たShiori

Agentは最初から大量ファイルを探索する必要がなくなる。

従来：

```text
find files
 ↓
grep
 ↓
read file
 ↓
grep again
 ↓
read another file
 ↓
find references
```

Shiori：

```text
search "SaveAccount"
 ↓
AccountService.cs:142
 ↓
navigate references
 ↓
5 locations
```

というフローを目標とする。

---

$2## 54. Agent向け最適化

MCP Tool descriptionには、

「どのような場合にこのToolを使うべきか」

を明確に記載する。

例えば`search`：

> Use this tool before manually traversing directories or repeatedly grepping the workspace. It combines file, symbol and text search and returns ranked code locations.

これによりClaude Code / CodexがShioriを優先利用しやすくする。

---

$2## 55. Tool重複の抑制

Claude Code / Codexは既に、

```text
file read
shell
grep
```

などを持つ。

そのためShioriは既存Toolを単純にMCPへ複製しない。

Shioriの価値を、

```text
Indexed Search
Symbol Search
Code Navigation
Query Planning
Ranking
```

に集中させる。

---

$2## 56. v1必須機能

Shiori v1完成条件：

```text
MCP Streamable HTTP server
Workspace isolation
SQLite database
File index
ripgrep search
Tree-sitter parsing
Symbol index
SQLite FTS5
Incremental indexing
File watcher
Generic search tool
File search
Text search
Symbol search
File outline
Index status
CLI
Windows support
Claude Code integration
Codex integration
```

---

$2## 57. v1.1候補

```text
LSP Definition
LSP References
LSP Implementations
Call hierarchy
AST pattern search
```

LSPについて実装が安定する場合はv1へ前倒し可能。

macOS正式対応・Linux正式対応は対応しない（2026.08.17決定）。Windowsを唯一の正式対象プラットフォームとする。

---

$2## 58. v2候補

```text
Cross-repository symbol graph
Dependency graph
Git-aware ranking
Recently changed code ranking
Search session cache
Remote MCP mode
Plugin system
Language adapters
Custom ranking
Repository relationship graph
```

### v2.1承認済み対象（2026.08.21）

最初のv2対象は、既存の統合検索へGit状態を使った順位補正を追加する。

```text
Git-aware ranking
Recently changed code ranking
```

完成条件：

- Gitで追跡されているファイルを、同等の検索一致度を持つ未追跡ファイルより優先する。
- 最近変更されたファイルへ限定的な加点を行い、検索一致度を逆転させすぎない。
- Gitリポジトリではない場合、Gitが利用できない場合、または情報取得に失敗した場合は現行順位を維持する。
- ワークスペース外のパスをGitへ渡さず、検索結果形式と既定設定を変更しない。
- 順位補正、フォールバック、境界条件を単体テストで検証する。

その他のv2候補は未承認として維持する。

---

$2## 59. Embeddingについて

v1ではEmbeddingを導入しない。

理由：

```text
インデックス生成負荷
モデル配布
検索結果の非決定性
DB容量
CPU/GPU負荷
```

Shioriの初期目標である

**高速な正確検索**

には、

```text
ripgrep
FTS5
Tree-sitter
LSP
```

で十分と判断する。

将来、

```text
semantic_search
```

として独立したoptional providerを追加する余地は残す。

---

$2## 60. Shioriの最終的な位置付け

Shioriは単なる検索コマンドラッパーではない。

```text
                AI Agent
                    │
                    ▼
                 Shiori
                    │
       ┌────────────┼────────────┐
       ▼            ▼            ▼
   File Index   Code Index   Semantic Nav
       │            │            │
   SQLite       Tree-sitter      LSP
                SQLite FTS5
                    │
                    ▼
                 ripgrep
```

として、

**AI Agentとローカルファイル群の間に存在する高速ファイル探索レイヤー**

を主機能とし、コードインデックスとSemantic Navigationを補助機能として提供する。

---

$2## 61. 一文での定義

> **Shiori is a fast, local-first file search MCP server that uses per-workspace SQLite indexes to help AI agents find the right files quickly, with ripgrep, Tree-sitter, FTS5 and LSP available as secondary code search and navigation capabilities.**

---

$2## 62. 開発優先順位

実装は以下の順序を推奨する。

```text
Phase 1
MCP + CLI
SQLite
Workspace
ripgrep
File Search

        ↓

Phase 2
Tree-sitter
Symbol extraction
FTS5
Incremental index
File watcher

        ↓

Phase 3
Query Planner
Unified search
Ranking

        ↓

Phase 4
LSP
Definition
References
Implementations
Call hierarchy

        ↓

Phase 5
Benchmark
Optimization
Cross-platform
Release
```

特に最初からLSPを中心に実装せず、

**SQLite + ripgrep + Tree-sitterでShioriの高速検索基盤を完成させてからLSPを追加する**

構成を推奨する。

これによりLSP Serverの有無に左右されず、Shiori単体で十分に価値のあるMCPサーバとして成立する。
