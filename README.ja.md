# Shiori

[English](README.md) | [日本語](README.ja.md)

Shioriは、AIエージェント向けの高速なローカルファースト・
ファイル検索サーバーです。ファイル本文を開かず、ファイル名、パス、
メタデータだけを索引化します。単一のStreamable HTTP MCP
エンドポイントを公開し、単一SQLiteデータベース内でワークスペースIDごとに
インデックスを分離します。

製品バージョン: `2.3.7`

## はじめに

1. 以下のいずれかの方法でShioriをインストールします。
2. MCPベアラートークンと許可ワークスペースを設定します。
3. 各ワークスペースの初期インデックスを作成します。
4. サーバーを起動し、AIエージェントへ接続します。

## インストール

### Windowsインストーラ

[最新リリース](https://github.com/katsushoe/Shiori/releases/latest)から
`shiori-v2.3.7-win-x64-setup.msi`をダウンロードして実行します。
**Add Shiori to the current user's PATH**を選択したまま進めてください。
インストーラは自己完結型で、現在のユーザーにのみインストールされます。
指定したインストールルートの下に`bin`、`config`、`logs`、`data`を作成し、
`bin`をPATHへ追加します。削除する場合はWindowsの「インストールされている
アプリ」を使用します。アンインストール後も設定、ログ、データは保持されます。
セットアップでアプリケーション言語を選択し、その値を`config\shiori.ini`へ
保存します。インストール後は新しいターミナルを開いてください。

```powershell
shiori doctor
```

### ZIPバイナリ

最新リリースから`shiori-v2.3.7-win-x64.zip`と隣接するSHA-256ファイルを
ダウンロードします。チェックサムを確認して任意の恒久的なインストールルートへ
展開し、その`bin`ディレクトリをユーザーの`PATH`へ追加してください。ZIPにも
インストーラと同じ標準構成が含まれ、.NETを別途インストールする必要はありません。

```powershell
$expected = (Get-Content .\shiori-v2.3.7-win-x64.zip.sha256).Split()[0]
$actual = (Get-FileHash .\shiori-v2.3.7-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
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
.\scripts\Publish-Windows.ps1 -Version 2.3.7
```

配布スクリプトはインストーラ、ZIP、チェックサムを`artifacts/`へ出力します。
WiX Toolset CLIを導入していない場合は`-SkipInstaller`を指定してください。

## 設定

32文字以上のランダムなトークンを作成し、MCPサーバーからのアクセスを許可する
全ディレクトリを登録します。

```powershell
$env:SHIORI_MCP_TOKEN = ([guid]::NewGuid().ToString('N'))
shiori workspace add F:\Projects\ProjectA
shiori workspace add F:\Projects\ProjectB
shiori doctor
```

ターミナルを閉じた後もサーバーを利用する場合は、安全なユーザー環境設定へ
トークンを永続化してください。登録済みワークスペースがMCPアクセス境界です。
全設定は[CONFIG.md（英語）](CONFIG.md)を参照してください。

## 使用方法

### ワークスペース登録と初期インデックスの作成

最初の検索前にワークスペースを登録します。登録時に独立したインデックスを
自動作成します。

```powershell
shiori workspace add F:\Projects\ProjectA
shiori workspace add F:\Projects\ProjectB
shiori index status F:\Projects\ProjectA
```

インデックス作成前に対象ディレクトリ数を数え、作成中は進捗率、処理済み/対象
ディレクトリ数、現在のファイル絶対パスをコンソールへ表示します。更新はMCPではなく
CLIから明示的に実行します。
ディレクトリごとの完了地点はSQLiteへ保存されます。中断した場合は、サーバー
起動時に未完了の世代を検出し、直前の完成済みインデックスを維持したまま
バックグラウンドで自動再開します。
正常公開後は、`index status`とMCPの`index_status` Toolが、実ディレクトリを
再走査せずSQLiteの永続状態から`indexed_directories`を返します。

CLIの`version`、`workspace list`、`index status`、`find`はMCPの読み取りToolに
対応します。`find`は`--allow`省略時に全登録ワークスペースを検索し、複数の
`--allow`を指定すると選択したワークスペースをまとめて検索します。

### 起動と接続

```powershell
shiori serve --port 39473
```

MCPエンドポイントは`http://127.0.0.1:39473/mcp`です。
`shiori config claude`または`shiori config codex`でクライアント設定を生成します。
どちらもトークン値を埋め込まず、環境変数名を参照します。
登録ワークスペースが0件でもサーバーは起動でき、一覧とヘルスチェックを利用
できます。ワークスペースを登録するまでは検索結果が0件になります。
認証済みMCPクライアントからもワークスペースの追加・削除、インデックス作成、
診断、クライアント設定生成を実行できます。WindowsでMCPからワークスペースを
追加すると、MCPサーバーがWindows Terminalを直接起動して索引作成の進捗を表示します。
ワークスペース追加はローカル
ファイルシステムのアクセス境界を拡張するため、Bearer Tokenを厳重に管理してください。

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
