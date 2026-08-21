# Shiori

[English](README.md) | [日本語](README.ja.md)

Shioriは、AIコーディングエージェント向けの高速なローカルファースト・
ファイル検索サーバーです。インデックス済みコード検索とセマンティック
ナビゲーションは副次機能として利用できます。単一のStreamable HTTP MCP
エンドポイントを公開し、ワークスペースごとに独立したSQLiteインデックスを
保持します。
統合検索はGit情報を利用できる場合、追跡中および最近変更されたファイルへ
限定的な順位加点を行います。

製品バージョン: `1.2.0`

## はじめに

1. 以下のいずれかの方法でShioriをインストールします。
2. MCPベアラートークンと許可ワークスペースを設定します。
3. 各ワークスペースの初期インデックスを作成します。
4. サーバーを起動し、AIコーディングエージェントへ接続します。

## インストール

### Windowsインストーラ

[最新リリース](https://github.com/katsushoe/Shiori/releases/latest)から
`shiori-v1.2.0-win-x64-setup.msi`をダウンロードして実行します。
**Add Shiori to the current user's PATH**を選択したまま進めてください。
インストーラは自己完結型で、現在のユーザーにのみインストールされます。
指定したインストールルートの下に`bin`、`config`、`logs`、`data`を作成し、
`bin`をPATHへ追加します。削除する場合はWindowsの「インストールされている
アプリ」を使用します。アンインストール後も設定、ログ、データは保持されます。
セットアップでアプリケーション言語を選択し、その値を`config\shiori.ini`へ
保存します。インストール後は新しいターミナルを開いてください。
ripgrep 15.2.0も同梱されるため、本文検索に別途インストールは不要です。

```powershell
shiori doctor
```

### ZIPバイナリ

最新リリースから`shiori-v1.2.0-win-x64.zip`と隣接するSHA-256ファイルを
ダウンロードします。チェックサムを確認して任意の恒久的なインストールルートへ
展開し、その`bin`ディレクトリをユーザーの`PATH`へ追加してください。ZIPにも
インストーラと同じ標準構成が含まれ、.NETを別途インストールする必要はありません。

```powershell
$expected = (Get-Content .\shiori-v1.2.0-win-x64.zip.sha256).Split()[0]
$actual = (Get-FileHash .\shiori-v1.2.0-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw "Checksum mismatch" }
```

### ソースからビルド

.NET 10 SDK、Rust stable toolchain、Visual Studio 2022 C++ Build Tools、Git、
および必要に応じてWiX Toolset CLI（`dotnet tool install --global wix`）を
インストールします。その後、次のコマンドでクローン、ビルド、テスト、
パッケージ作成を実行します。

```powershell
git clone https://github.com/katsushoe/Shiori.git
Set-Location Shiori
cargo build --release --manifest-path .\native\shiori-engine\Cargo.toml
dotnet restore .\Shiori.slnx
dotnet build .\Shiori.slnx --configuration Release --no-restore
dotnet test .\tests\Shiori.Core.Tests\Shiori.Core.Tests.csproj --configuration Release --no-build
.\scripts\Publish-Windows.ps1 -Version 1.2.0
```

配布スクリプトはインストーラ、ZIP、チェックサムを`artifacts/`へ出力します。
WiX Toolset CLIを導入していない場合は`-SkipInstaller`を指定してください。

## 設定

32文字以上のランダムなトークンを作成し、MCPサーバーからのアクセスを許可する
全ディレクトリを設定します。Windowsでは複数のパスを`;`で区切ります。

```powershell
$env:SHIORI_MCP_TOKEN = ([guid]::NewGuid().ToString('N'))
$env:SHIORI_ALLOWED_WORKSPACES = 'F:\Projects\ProjectA;F:\Projects\ProjectB'
shiori doctor
```

ターミナルを閉じた後もサーバーを利用する場合は、安全なユーザー環境設定へ
値を永続化してください。`workspace add`による登録はMCPアクセスを許可しません。
全設定は[CONFIG.md（英語）](CONFIG.md)を参照してください。

## 使用方法

### 初期インデックスの作成

最初の検索前に、ワークスペースごとに独立したインデックスを作成します。

```powershell
shiori index build --allow F:\Projects\ProjectA
shiori index build --allow F:\Projects\ProjectB
shiori index status --allow F:\Projects\ProjectA
```

2回目以降の`index build`は差分更新になります。MCPクライアントでは
`update_indexes`を使って選択したワークスペース、または許可された全
ワークスペースを更新できます。応答は全更新の完了後に返ります。

### 起動と接続

```powershell
shiori serve --port 39473
```

MCPエンドポイントは`http://127.0.0.1:39473/mcp`です。
`shiori config claude`または`shiori config codex`でクライアント設定を生成します。
どちらもトークン値を埋め込まず、環境変数名を参照します。

## ドキュメント

- [CLIコマンドリファレンス](COMMANDS.ja.md)
- [設定リファレンス](CONFIG.ja.md)
- [パッケージ一覧（英語）](PACKAGES.md)
- [MCP設定ガイド](MCP_SETUP.ja.md)
- [セキュリティポリシー（英語）](SECURITY.md)
- [アーキテクチャ（英語）](docs/architecture.md)
- [仕様書（日本語）](docs/specification.ja.md)
- [マルチワークスペース連携ADR（英語）](docs/adr/0002-multi-workspace-coordination.md)

## セキュリティ

Shioriはループバックだけで待ち受け、MCPではベアラー認証を必須とし、明示的に
許可されたワークスペースルート外へのアクセスを拒否します。ベアラートークンや
実環境の設定値をコミットしないでください。

## ライセンス

Shioriは[MIT License](LICENSE)で提供されます。
