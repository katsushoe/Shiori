# Shiori MCP設定

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

このガイドでは、ローカルで動作するShioriサーバーをAIコーディングエージェントへ
接続します。全環境変数は[CONFIG.ja.md](CONFIG.ja.md)、CLIの詳細は
[COMMANDS.ja.md](COMMANDS.ja.md)を参照してください。

## 値とプレースホルダー

| 値 | 取得方法 | 例 | 変更条件 |
| :--- | :--- | :--- | :--- |
| MCPトークン | 32文字以上のランダム値を生成 | 生成したGUID | 認証情報の新規作成・ローテーション時 |
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

## 認証と環境設定

32文字以上のベアラートークンを作成し、MCPからアクセスできるワークスペース
ルートを登録します。

```powershell
$env:SHIORI_MCP_TOKEN = ([guid]::NewGuid().ToString('N'))
shiori workspace add F:\Projects\One
shiori workspace add F:\Projects\Two
shiori doctor
```

クライアントとサーバーのプロセスには同じトークンを設定してください。トークンを
コミット対象の設定ファイルへ書かないでください。中央`Workspaces`テーブルが
サーバー境界を定義します。

## サーバー起動

```powershell
shiori serve --port 39473
```

プロセスはループバックだけで待ち受けます。MCPエンドポイントは
`http://127.0.0.1:39473/mcp`、認証不要のローカルヘルスエンドポイントは
`http://127.0.0.1:39473/health`です。利用中はサーバープロセスを実行し続けます。

## クライアント登録

クライアント登録はShioriを検出できる範囲を制御します。ファイルアクセスは
`shiori workspace add`で登録したワークスペースだけに制限されます。

### Claude Code（推奨）

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

変更後はClaude Codeを再起動または再読込し、`/mcp`で確認します。

### Claude Code生成コマンド（代替）

Claude Codeのプロジェクトルートで実行すると、同じプロジェクトスコープの
`.mcp.json`内容を生成します。

```powershell
shiori config claude > .mcp.json
```

リダイレクトはファイルを上書きするため、新規作成時だけ使用してください。
`.mcp.json`が存在する場合は、生成された`mcpServers.shiori`項目を統合します。
`SHIORI_MCP_TOKEN`を持つ環境からClaude Codeを起動し、変更後は再起動または
再読込して`/mcp`で確認します。

### Codex（推奨）

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

変更後はCodexを再起動するか、新しいタスクを開始します。

### Codex生成コマンド（代替）

同じユーザースコープのTOMLセクションを出力します。

```powershell
shiori config codex
```

他のCodex設定を置き換えずに出力を統合し、Codexへ`SHIORI_MCP_TOKEN`を渡して、
Codexを再起動するか新しいタスクを開始します。

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

サーバーとクライアントが同じ`SHIORI_MCP_TOKEN`を継承しているか確認します。
トークンは32文字以上必要です。変更後は両方のプロセスを再起動してください。

### ワークスペースが拒否または未検出

`shiori workspace add <パス>`で存在する絶対ディレクトリを登録し、サーバーを
再起動します。認可対象は`shiori workspace list`で確認できます。

### 接続拒否

`shiori serve`が動作中で、クライアントとサーバーのポートが一致し、URLが
`http://127.0.0.1:<port>/mcp`であることを確認します。

### 検索結果が古い

`shiori index build --allow <workspace>`を実行します。明示的な置き換えが必要な場合は
`index rebuild`を使用します。
