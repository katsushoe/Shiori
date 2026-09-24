# Shiori MCP設定

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

このガイドでは、ローカルで動作するShioriサーバーをAIコーディングエージェントへ
接続します。全設定は[CONFIG.ja.md](CONFIG.ja.md)、CLIの詳細は
[COMMANDS.ja.md](COMMANDS.ja.md)を参照してください。

## 値とプレースホルダー

| 値 | 取得方法 | 例 | 変更条件 |
| :--- | :--- | :--- | :--- |
| ワークスペースパス | 存在する絶対ディレクトリパスを取得 | `F:\Projects\One` | 別のワークスペースを許可する時 |
| ポート | 未使用のループバックTCPポートを選択 | `39473` | 既定ポートが使用できない時 |
| サーバー名 | クライアント表示用識別子を選択 | `shiori` | 複数のShioriサーバーを登録する時 |

`<workspace>`など山括弧付きの値はプレースホルダーです。実際の値へ置き換え、
山括弧をそのまま入力しないでください。

## 前提条件

- [README.ja.md](README.ja.md)のインストーラ、ZIP、またはソース手順でShioriを
  インストールします。
- 存在する絶対ディレクトリをワークスペースとして1つ以上選びます。
- インストーラで`PATH`へ追加した場合は、新しいターミナルを開きます。

## 認証

MCPからアクセスできるワークスペースルートを登録します。

```powershell
shiori workspace add F:\Projects\One
shiori workspace add F:\Projects\Two
shiori doctor
```

トークンの設定は不要です。初回の`shiori serve`または`shiori mcp`実行時に、
ランダムなベアラートークンを現在のユーザー用のWindows DPAPIで暗号化し、
`config\mcp-token.bin`へ保存します。クライアントはトークンを扱わず、
stdioブリッジ`shiori mcp`がトークンを読み込んでサーバーへ要求を中継します。
サーバーとクライアントは同じWindowsユーザーで実行してください。中央
`Workspaces`テーブルがサーバー境界を定義します。

## サーバー起動

```powershell
shiori serve --port 39473
```

プロセスはループバックだけで待ち受けます。MCPエンドポイントは
`http://127.0.0.1:39473/mcp`、認証不要のローカルヘルスエンドポイントは
`http://127.0.0.1:39473/health`です。利用中はサーバープロセスを実行し続けます。

Shioriは初期化時に、目的、推奨検索手順、本文検索を行わない制約、変更操作の
安全上の注意をServer Instructionsとして提供します。MCP Prompt対応クライアント
では`shiori_guide`を開くと、検索、インデックス保守、ワークスペース管理の
実践的な使い方を確認できます。

## クライアント登録

クライアント登録はShioriを検出できる範囲を制御します。ファイルアクセスは
`shiori workspace add`で登録したワークスペースだけに制限されます。どちらの
クライアントも`shiori mcp`を起動するため、`shiori`が`PATH`上に必要です。

### Claude Code（推奨）

次の完全なプロジェクト設定を、Claude Codeプロジェクトルートの`.mcp.json`へ
保存または統合します。

```json
{
  "mcpServers": {
    "shiori": {
      "type": "stdio",
      "command": "shiori",
      "args": ["mcp", "--port", "39473"]
    }
  }
}
```

`shiori`はクライアントに表示されるサーバー名、`type`はstdio Transportです。
`--port`は`shiori serve`へ渡したポートに合わせます。秘密値を含まないため、
このファイルはコミットできます。

変更後はClaude Codeを再起動または再読込し、`/mcp`で確認します。

### Claude Code生成コマンド（代替）

Claude Codeのプロジェクトルートで実行すると、同じプロジェクトスコープの
`.mcp.json`内容を生成します。

```powershell
shiori config claude > .mcp.json
```

リダイレクトはファイルを上書きするため、新規作成時だけ使用してください。
`.mcp.json`が存在する場合は、生成された`mcpServers.shiori`項目を統合します。
変更後はClaude Codeを再起動または再読込して`/mcp`で確認します。

### Codex（推奨）

次の完全なサーバー設定を`%USERPROFILE%\.codex\config.toml`へ追加します。

```toml
[mcp_servers.shiori]
command = "shiori"
args = ["mcp", "--port", "39473"]
```

`shiori`はクライアントに表示されるサーバー名です。`command`と`args`で
stdioブリッジを起動し、`--port`は`shiori serve`へ渡したポートに合わせます。
TOMLに秘密値は含まれません。

変更後はCodexを再起動するか、新しいタスクを開始します。

### Codex生成コマンド（代替）

同じユーザースコープのTOMLセクションを出力します。

```powershell
shiori config codex
```

他のCodex設定を置き換えずに出力を統合し、Codexを再起動するか新しいタスクを
開始します。

## 複数ワークスペース

各ワークスペースを登録します。すべてのインデックスは統合SQLiteデータベースへ
保存されます。

```powershell
shiori workspace add F:\Projects\One
shiori workspace add F:\Projects\Two
```

ファイルインデックスの更新が必要なときはCLIを再実行します。MCPツールは
読み取り専用であり、インデックス処理を開始しません。
クライアントスコープとワークスペース認可は独立しており、登録済みクライアントも
中央`Workspaces`テーブルにないパスへはアクセスできません。

## 接続確認

最初に失敗した段階で停止し、解消後に次へ進みます。

1. `http://127.0.0.1:39473/health`を開きます。合格条件はHTTP `200`と正常状態です。
2. クライアントに`shiori`サーバーとTool一覧が表示されることを確認します。合格条件は
   接続エラーと認証エラーがないことです。
3. 読み取り専用の`workspace_list`を呼びます。合格条件は想定した許可ルートだけが
   返ることです。
4. 読み取り専用の`search_files`で既知のファイル名を検索します。合格条件は
   ワークスペース識別情報付きで対象ファイルが返ることです。
5. `shiori doctor`を実行します。合格条件は必須Checkが`ok`であることです。

`search_files`は1つ、複数、または許可された全ワークスペースを対象にできます。
結果にはワークスペース識別情報が含まれ、異なるルートの同一相対パスを区別できます。

## トラブルシューティング

### 認証エラー

サーバーとMCPクライアントが同じWindowsユーザーで動作しているか確認します。
別ユーザーではブリッジがトークンを復号できません。サーバー稼働中にトークンを
ローテーションした場合は、サーバーとクライアントを再起動してください。現在の
ユーザーで復号できない場合、`shiori doctor`は`mcp_token`を`error`と報告します。

### ワークスペースが拒否または未検出

`shiori workspace add <パス>`で存在する絶対ディレクトリを登録し、サーバーを
再起動します。認可対象は`shiori workspace list`で確認できます。

### 接続拒否

`shiori serve`が動作中で、クライアント設定の`--port`がサーバーのポートと
一致していることを確認します。サーバーへ接続できない場合、ブリッジは
JSON-RPCエラーを返します。

### 検索結果が古い

`shiori index build --allow <workspace>`を実行します。明示的な置き換えが必要な場合は
`index rebuild`を使用します。
