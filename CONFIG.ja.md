# Shiori設定リファレンス

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

この文書はShioriの実行時設定の正本です。製品設定は`config\shiori.ini`から
読み込み、MCPトークンはDPAPIで保護したファイルに保存します。Shioriは環境変数を
読み込みません。

## 設定ディレクトリ

インストーラとZIPは、指定したインストールルートの下に`bin`、`config`、`logs`、
`data`を配置します。ワークスペース登録とインデックスは既定で`data`へ保存されます。
保存先は変更できません。

## ファイル生成

- Windowsインストーラは`shiori config init`を実行し、`config\shiori.ini`が
  存在しない場合だけ、セットアップ中に選択した言語で作成します。アップグレードと
  アンインストールはこのファイルを変更・削除しません。サイレントインストールの
  初期値は`en-US`です。
  日本語を指定する場合は、MSI公開プロパティ`SHIORI_LANGUAGE=ja-JP`を使用します。
- `shiori.db`には中央`Workspaces`テーブルと全ワークスペースのインデックスを
  保存します。旧`workspaces.json`、`workspaces.db`、ワークスペース別DBは
  一度だけ移行します。
- ClaudeとCodexの設定断片は`shiori config`が標準出力へ表示します。保存または
  統合する場所は利用者が決定します。

## 主要設定

### `config\shiori.ini`

```ini
[general]
language=ja-JP

[index]
exclude_patterns=generated/**;*.min.js
```

| 設定 | 必須 | 型 | 既定値 | 制約 |
| :--- | :--- | :--- | :--- | :--- |
| `general.language` | 任意 | ロケール識別子 | `en-US` | `en-US`または`ja-JP` |
| `index.exclude_patterns` | 任意 | パターンリスト | なし | `;`区切りのgitignore形式パターン |

ファイルが存在しない場合は`en-US`を使用します。未対応の値は`shiori doctor`で
エラーとして報告されます。設定言語はプロセス起動時に適用され、CLIヘルプと
利用者向けのコマンドエラーを切り替えます。コマンド名、JSONフィールド名、ログ、
MCPプロトコル値は言語に依存しません。

### `index.exclude_patterns`

追加するgitignore形式パターンの任意リストです。`;`で区切り、`.gitignore`および
Shiori既定のビルド・依存ディレクトリ除外と組み合わせて使用します。省略時は
利用者定義のパターンを追加しません。値はプロセスがワークスペースを開くときに
読み込まれるため、変更後は`shiori serve`を再起動してください。

### MCPトークンファイル

| ファイル | 作成契機 | 保護 | 内容 |
| :--- | :--- | :--- | :--- |
| `config\mcp-token.bin` | 初回の`shiori serve`または`shiori mcp`実行 | DPAPI（現在のWindowsユーザー） | 256ビットのランダムなベアラートークン |

サーバーはこのトークンで`/mcp`を認証し、stdioブリッジ`shiori mcp`も同じ
ファイルを読み込みます。トークンを手動で設定する項目はありません。
ローテーションする場合は、サーバーを停止してファイルを削除し、サーバーとMCP
クライアントを再起動します。`shiori doctor`は、ファイル作成前の`mcp_token`を
`warning`、現在のユーザーで復号できない場合を`error`として報告します。

## プロファイル設定

Shioriに名前付き実行プロファイルはありません。Claude CodeとCodexの生成設定は
`shiori mcp --port 39473`を起動し、ブリッジが保護されたトークンを付けて
`http://127.0.0.1:39473/mcp`へ中継します。

## 設定例

```ini
[general]
language=ja-JP

[index]
exclude_patterns=generated/**;*.min.js
```

```powershell
shiori workspace add F:\Projects\One
shiori workspace add F:\Projects\Two
shiori doctor
shiori serve --port 39473
```
