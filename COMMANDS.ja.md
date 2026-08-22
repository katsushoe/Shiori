# Shioriコマンドリファレンス

Shiori v2はファイル名とパスの検索だけを提供します。インデックス処理は
ファイル本文を開かず、ディレクトリエントリとファイルメタデータを読み取ります。

## CLI

### `shiori find`

```powershell
shiori find <クエリ> --allow <絶対ディレクトリ> [--limit <1-100>]
```

作成済みSQLiteインデックスから、ファイル名または相対パスの一部を検索します。

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

MCPツールはすべて読み取り専用です。進捗をコンソールで確認できるよう、
インデックス作成はCLIから明示的に実行します。
