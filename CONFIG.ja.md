# Shiori設定リファレンス

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

この文書はShioriの実行時設定の正本です。秘密情報を含まない製品設定は
`config\shiori.ini`から読み込み、運用設定や秘密情報は環境変数から読み込みます。

## 設定ディレクトリ

インストーラとZIPは、指定したインストールルートの下に`bin`、`config`、`logs`、
`data`を配置します。ワークスペース登録とインデックスは既定で`data`へ保存されます。
保存先を変更する場合は`SHIORI_DATA_HOME`を設定します。

## ファイル生成

- Windowsインストーラは、セットアップ中に選択した言語を記録した
  `config\shiori.ini`を作成します。サイレントインストールの初期値は`en-US`です。
  日本語を指定する場合は、MSI公開プロパティ`SHIORI_LANGUAGE=ja-JP`を使用します。
- `workspaces.json`は`shiori workspace`コマンドが作成・更新します。
- `indexes\<workspace-id>\shiori.db`はインデックス操作が作成します。
- ClaudeとCodexの設定断片は`shiori config`が標準出力へ表示します。保存または
  統合する場所は利用者が決定します。

## 主要設定

### `config\shiori.ini`

```ini
[general]
language=ja-JP
```

| 設定 | 必須 | 型 | 既定値 | 制約 |
| :--- | :--- | :--- | :--- | :--- |
| `general.language` | 任意 | ロケール識別子 | `en-US` | `en-US`または`ja-JP` |

ファイルが存在しない場合は`en-US`を使用します。未対応の値は`shiori doctor`で
エラーとして報告されます。設定言語はプロセス起動時に適用され、CLIヘルプと
利用者向けのコマンドエラーを切り替えます。コマンド名、JSONフィールド名、ログ、
MCPプロトコル値は言語に依存しません。

### 環境変数

| 設定 | 必須 | 型 | 既定値 | 制約 |
| :--- | :--- | :--- | :--- | :--- |
| `SHIORI_MCP_TOKEN` | `serve`で必須 | 文字列 | なし | 32文字以上 |
| `SHIORI_ALLOWED_WORKSPACES` | `serve`で必須 | パスリスト | なし | 存在する絶対ディレクトリ |
| `SHIORI_DATA_HOME` | 任意 | 絶対パス | `<インストールルート>\data` | 書き込み可能なディレクトリ |
| `SHIORI_EXCLUDE_PATTERNS` | 任意 | パターンリスト | なし | `;`区切りのgitignore形式パターン |

環境変数は`shiori serve`プロセスの起動時に読み込まれます。変更後はサーバーを
再起動してください。

### `SHIORI_MCP_TOKEN`

`/mcp`で使用するベアラートークンです。既定値はなく、32文字以上の文字列が
必要です。省略すると`serve`は起動しません。秘密として管理し、MCPクライアント
プロセスにも同じ環境変数を設定します。

```powershell
$env:SHIORI_MCP_TOKEN = ([guid]::NewGuid().ToString('N'))
```

### `SHIORI_ALLOWED_WORKSPACES`

MCPファイルアクセスの認可境界です。存在する絶対ディレクトリをOSのパス区切り
文字（Windowsでは`;`）で連結します。既定値はなく、省略すると`serve`は起動
しません。CLIの`--allow`やワークスペース登録はこのリストを拡張しません。

```powershell
$env:SHIORI_ALLOWED_WORKSPACES = 'F:\Projects\One;F:\Projects\Two'
```

### `SHIORI_DATA_HOME`

`workspaces.json`とワークスペース別SQLiteデータベースを保存する任意の絶対
ディレクトリです。既定値はインストールルート直下の`data`です。必要時に作成される
ため、現在のユーザーに書き込み権限が必要です。

```powershell
$env:SHIORI_DATA_HOME = 'D:\ShioriData'
```

### `SHIORI_EXCLUDE_PATTERNS`

追加するgitignore形式パターンの任意リストです。`;`で区切り、`.gitignore`および
Shiori既定のビルド・依存ディレクトリ除外と組み合わせて使用します。省略時は
利用者定義のパターンを追加しません。

```powershell
$env:SHIORI_EXCLUDE_PATTERNS = 'generated/**;*.min.js'
```

## プロファイル設定

Shioriに名前付き実行プロファイルはありません。Claude CodeとCodexの生成設定は、
既定で`http://127.0.0.1:39473/mcp`を参照し、`SHIORI_MCP_TOKEN`を環境変数から
読み込みます。

## 設定例

```powershell
$env:SHIORI_MCP_TOKEN = ([guid]::NewGuid().ToString('N'))
$env:SHIORI_ALLOWED_WORKSPACES = 'F:\Projects\One;F:\Projects\Two'
$env:SHIORI_EXCLUDE_PATTERNS = 'generated/**;*.min.js'
shiori doctor
shiori serve --port 39473
```
