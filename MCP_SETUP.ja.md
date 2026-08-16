# Shiori MCP設定

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

このガイドでは、ローカルで動作するShioriサーバーをAIコーディングエージェントへ
接続します。全環境変数は[CONFIG.ja.md](CONFIG.ja.md)、CLIの詳細は
[COMMANDS.ja.md](COMMANDS.ja.md)を参照してください。

## 前提条件

- [README.ja.md](README.ja.md)のインストーラ、ZIP、またはソース手順でShioriを
  インストールします。
- 存在する絶対ディレクトリをワークスペースとして1つ以上選びます。
- インストーラで`PATH`へ追加した場合は、新しいターミナルを開きます。

## サーバー設定

32文字以上のベアラートークンを作成し、ワークスペースルートを許可します。
Windowsでは複数のパスを`;`で区切ります。

```powershell
$env:SHIORI_MCP_TOKEN = ([guid]::NewGuid().ToString('N'))
$env:SHIORI_ALLOWED_WORKSPACES = 'F:\Projects\One;F:\Projects\Two'
shiori doctor
```

クライアントとサーバーのプロセスには同じトークンを設定してください。トークンを
コミット対象の設定ファイルへ書かないでください。`workspace add`はMCPアクセスを
許可せず、サーバー境界は`SHIORI_ALLOWED_WORKSPACES`だけで決まります。

## 初期インデックスの作成

ワークスペースごとに永続SQLiteインデックスを作成します。

```powershell
shiori index build --allow F:\Projects\One
shiori index build --allow F:\Projects\Two
```

2回目以降は差分更新です。MCPクライアントは`update_indexes`を呼び出して、選択した
ワークスペースまたは許可された全ワークスペースを更新できます。応答は全更新の
完了後に返ります。

## サーバー起動

```powershell
shiori serve --port 39473
```

プロセスはループバックだけで待ち受けます。MCPエンドポイントは
`http://127.0.0.1:39473/mcp`、認証不要のローカルヘルスエンドポイントは
`http://127.0.0.1:39473/health`です。利用中はサーバープロセスを実行し続けます。

## Claude Code

次の完全なプロジェクト設定を、Claude Codeプロジェクトルートの`.mcp.json`へ
保存または統合します。

```json
{
  "mcpServers": {
    "shiori": {
      "type": "http",
      "url": "http://127.0.0.1:39473/mcp",
      "headers": {
        "Authorization": "Bearer ${SHIORI_MCP_TOKEN}"
      }
    }
  }
}
```

`shiori`はクライアントに表示されるサーバー名、`type`はHTTP Transport、`url`は
`shiori serve`へ渡したポートに合わせる接続先です。認証ヘッダーはクライアント
プロセスの環境変数からトークンを参照します。秘密値へ置き換えないでください。

簡便手段として、Shioriは同じJSONを生成できます。

```powershell
shiori config claude > .mcp.json
```

リダイレクトはファイルを上書きするため、新規作成時だけ使用してください。
`.mcp.json`が存在する場合は、生成された`mcpServers.shiori`項目を統合します。
`SHIORI_MCP_TOKEN`を持つ環境からClaude Codeを起動し、変更後は再起動または
再読込して`/mcp`で確認します。

## Codex

次の完全なサーバー設定を`%USERPROFILE%\.codex\config.toml`へ追加します。

```toml
[mcp_servers.shiori]
url = "http://127.0.0.1:39473/mcp"
bearer_token_env_var = "SHIORI_MCP_TOKEN"
```

`shiori`はクライアントに表示されるサーバー名です。`url`を指定するとHTTP
Transportになり、`shiori serve`へ渡したポートに合わせます。Shioriは別プロセスで
起動するためローカル起動Commandは不要です。`bearer_token_env_var`により、秘密値を
TOMLへ保存せず、Codexプロセスの環境変数からベアラートークンを参照します。

簡便手段として、Shioriは同じTOMLセクションを生成できます。

```powershell
shiori config codex
```

他のCodex設定を置き換えずに出力を統合し、Codexへ`SHIORI_MCP_TOKEN`を渡して、
Codexを再起動するか新しいタスクを開始します。

## 接続確認

1. クライアントにShiori MCPサーバーとTool一覧が表示されることを確認します。
2. `workspace_list`を呼び、許可ルートを確認します。
3. `search_files`で既知のファイル名を検索します。
4. `update_indexes`を呼び、対象ワークスペースがすべて完了することを確認します。

`search_files`は1つ、複数、または許可された全ワークスペースを対象にできます。
結果にはワークスペース識別情報が含まれ、異なるルートの同一相対パスを区別できます。

## トラブルシューティング

### 認証エラー

サーバーとクライアントが同じ`SHIORI_MCP_TOKEN`を継承しているか確認します。
トークンは32文字以上必要です。変更後は両方のプロセスを再起動してください。

### ワークスペースが拒否または未検出

`SHIORI_ALLOWED_WORKSPACES`へ存在する絶対パスを設定し、Windowsでは`;`で区切ります。
変更後はサーバーを再起動します。CLIの登録はMCPアクセスを許可しません。

### 接続拒否

`shiori serve`が動作中で、クライアントとサーバーのポートが一致し、URLが
`http://127.0.0.1:<port>/mcp`であることを確認します。

### 検索結果が古い

`update_indexes`を呼ぶか、`shiori index build --allow <workspace>`を実行します。
完全な再走査が必要な場合だけ`index rebuild`を使用します。

### セマンティックナビゲーションが利用できない

ファイル検索とインデックス済みコード検索に言語サーバーは不要です。C#の
セマンティックナビゲーションには`csharp-ls`またはOmniSharpを導入し、必要なら
実行ファイルの絶対パスを`SHIORI_CSHARP_LSP_PATH`へ設定します。
