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

Claude Codeのプロジェクトディレクトリで、プロジェクト設定を生成します。

```powershell
shiori config claude > .mcp.json
```

`SHIORI_MCP_TOKEN`を持つ環境からClaude Codeを起動します。`.mcp.json`変更後は
再起動し、`/mcp`で確認します。生成JSONは環境変数を参照し、値を含みません。

## Codex

TOMLセクションを生成します。

```powershell
shiori config codex
```

出力を`%USERPROFILE%\.codex\config.toml`へ統合し、Codexへ
`SHIORI_MCP_TOKEN`を渡して新しいタスクを開始します。生成設定は
`bearer_token_env_var = "SHIORI_MCP_TOKEN"`を使用します。

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
