# Shioriコマンドリファレンス

Shiori v2はファイル名とパスの検索だけを提供します。インデックス処理は
ファイル本文を開かず、ディレクトリエントリとファイルメタデータを読み取ります。

## CLI

### `shiori version`

MCPの`get_version`と同じサーバー名および4要素バージョンを返します。

### `shiori find`

```powershell
shiori find <クエリ> [--allow <絶対ディレクトリ> ...] [--limit <1-100>]
```

作成済みSQLiteインデックスから、ファイル名または相対パスの一部を検索します。
`--allow`を省略すると全登録ワークスペースを検索します。複数指定すると選択した
集合を検索し、結果とワークスペース別エラーはMCPの`search_files`と一致します。

### `shiori index build`

```powershell
shiori index build --allow <絶対ディレクトリ>
```

除外後のディレクトリ数を数え、`完了数/総数 (パーセント)`形式で進捗を表示し、
新しいインデックス世代を公開します。メタデータは上限付きバッチでSQLiteへ
逐次保存します。失敗した場合も直前の正常なインデックスを検索できます。

### `shiori index rebuild`

`index build`と同じ可視化・逐次保存方式でインデックスを明示的に再構築します。

### `shiori index status`

ワークスペースID、状態、ファイル数、インデックス版、走査日時を返します。

### ワークスペースおよびサーバー

```powershell
shiori workspace add <絶対ディレクトリ>
shiori workspace list
shiori workspace remove <名前、ID、絶対ディレクトリ>
shiori doctor
shiori config claude [--port <1-65535>] [--name <サーバー名>]
shiori config codex [--port <1-65535>] [--name <サーバー名>]
shiori serve [--port <1-65535>]
```

`workspace add`はワークスペースを登録し、コンソールに進捗を表示しながら
インデックスを自動的に再構築します。登録済みワークスペースがMCPアクセス境界です。
`workspace remove`は登録とインデックスの両方を削除します。
移行した複数ワークスペースで名前が重複する場合は、IDまたは絶対パスを指定します。

## MCPツール

- `get_version`: 稼働中のShiori名とバージョンを返します。
- `workspace_list`: 許可ワークスペースとデータベースを列挙します。
- `index_status`: 許可ワークスペース1件のインデックス状態を返します。
- `search_files`: 1件、複数、または全許可ワークスペースを検索します。
- `workspace_add`: ディレクトリを登録し、稼働中のアクセス境界へ追加します。
  WindowsではMCPサーバーがWindows Terminalを直接起動し、初回インデックスの
  作成進捗を表示します。Windows以外ではMCP要求内で初回インデックスを作成します。
- `workspace_remove`: 稼働中サーバーからワークスペースとインデックスを削除します。
- `index_build`: ワークスペースのインデックスを作成して公開します。
- `index_rebuild`: ワークスペースのインデックスを再構築して置換します。
- `doctor`: Native、SQLite、ディレクトリ、設定、Token、ワークスペースを診断します。
- `config_claude`: Claude Code用MCP設定を生成します。
- `config_codex`: Codex用MCP設定を生成します。

検索4 Toolと診断・設定3 Toolは読み取り専用です。ワークスペースおよび
インデックス管理ToolはローカルSQLiteを変更し、全MCP要求と同じBearer Tokenを
必要とします。`workspace_add`はファイルシステムのアクセス境界を拡張するため、
Tokenを厳重に管理してください。`serve`はMCPホスト自身を起動するためCLI専用です。
