# Shiori

[English](README.md) | [日本語](README.ja.md)

Shiori is a fast, local-first file search server for AI coding agents. It
indexes file names, paths, and metadata without opening file contents.
The server exposes a single Streamable HTTP MCP endpoint and keeps every
workspace index isolated by workspace ID in one SQLite database.

Product version: `2.3.3`.

## Getting Started

1. Install Shiori using one of the methods below.
2. Configure the MCP bearer token and allowed workspaces.
3. Build the initial index for each workspace.
4. Start the server and connect your AI coding agent.

## Installation

### Windows installer

Download `shiori-v2.3.3-win-x64-setup.msi` from the
[latest release](https://github.com/katsushoe/Shiori/releases/latest), run it,
and keep **Add Shiori to the current user's PATH** selected. The installer is
self-contained, installs only for the current user, and uses `bin`, `config`,
`logs`, and `data` below the selected installation root. The installer adds
`bin` to PATH and can be removed from Windows **Installed apps**. Configuration,
logs, and data remain after uninstall. Open a new terminal after installation.
Setup asks for the application language and stores the selection in
`config\shiori.ini`.

```powershell
shiori doctor
```

### ZIP binary

Download `shiori-v2.3.3-win-x64.zip` from the latest release, verify the adjacent
SHA-256 file, extract it to a permanent installation root, and add its `bin`
directory to your user `PATH`. The ZIP contains the same standard directory
layout as the installer and does not require a separate .NET installation.

```powershell
$expected = (Get-Content .\shiori-v2.3.3-win-x64.zip.sha256).Split()[0]
$actual = (Get-FileHash .\shiori-v2.3.3-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw "Checksum mismatch" }
```

### Build from source

Install the .NET 10 SDK, Rust stable toolchain, Visual Studio 2022 C++ Build
Tools, Git, and optionally the WiX Toolset CLI (`dotnet tool install --global
wix`). Then clone and build:

```powershell
git clone https://github.com/katsushoe/Shiori.git
Set-Location Shiori
cargo build --release --manifest-path .\native\shiori-engine\Cargo.toml
dotnet restore .\Shiori.slnx
dotnet build .\Shiori.slnx --configuration Release --no-restore
dotnet test .\tests\Shiori.Core.Tests\Shiori.Core.Tests.csproj --configuration Release --no-build
.\scripts\Publish-Windows.ps1 -Version 2.3.3
```

The publish script writes the installer, ZIP, and checksum files to
`artifacts/`. Use `-SkipInstaller` when the WiX Toolset CLI is not installed.

## Configuration

Create a random token of at least 32 characters and register every directory
that the MCP server may access.

```powershell
$env:SHIORI_MCP_TOKEN = ([guid]::NewGuid().ToString('N'))
shiori workspace add F:\Projects\ProjectA
shiori workspace add F:\Projects\ProjectB
shiori doctor
```

Persist the token using a secure user-level environment configuration if the
server must survive terminal restarts. Registered workspaces are the MCP access
boundary. See [CONFIG.md](CONFIG.md) for every setting.

## Usage

### Register and Build the Initial Index

Register each workspace before the first search. Registration automatically
builds its independent index:

```powershell
shiori workspace add F:\Projects\ProjectA
shiori workspace add F:\Projects\ProjectB
shiori index status --allow F:\Projects\ProjectA
```

Shiori counts included directories before indexing and prints directory-level
progress in the console. Index updates are explicit CLI operations, not MCP
operations. Directory checkpoints are stored in SQLite. If indexing is
interrupted, the server detects the unfinished generation at startup and
resumes it in the background without replacing the last complete index.

CLI `version`, `workspace list`, `index status`, and `find` correspond to the
MCP read tools. `find` searches all registered workspaces when `--allow` is
omitted and accepts repeated `--allow` options for a selected set.

### Start and Connect

```powershell
shiori serve --port 39473
```

The MCP endpoint is `http://127.0.0.1:39473/mcp`. Generate client configuration
with `shiori config claude` or `shiori config codex`; both reference the token by
environment-variable name and never embed its value. The server can start with
no registered workspaces; workspace listing and health checks remain available,
while searches return no results until a workspace is registered.

Authenticated MCP clients can also add or remove workspaces, build indexes,
run diagnostics, and generate client configuration. On Windows, adding a workspace
through MCP makes the MCP server directly open Windows Terminal to display indexing
progress. Protect the bearer token: adding a workspace expands the server's local
filesystem access boundary.

## Documentation

- [CLI command reference](COMMANDS.md)
- [Configuration reference](CONFIG.md)
- [Package inventory](PACKAGES.md)
- [MCP setup guide](MCP_SETUP.md)
- [Security policy](SECURITY.md)
- [Architecture](docs/architecture.md)
- [Specification (Japanese)](docs/specification.ja.md)
- [Multi-workspace coordination ADR](docs/adr/0002-multi-workspace-coordination.md)

## Security

Shiori listens only on loopback, requires bearer authentication for MCP, and
rejects access outside explicitly allowed workspace roots. Do not commit bearer
tokens or real environment settings.

## License

Shiori is licensed under the [MIT License](LICENSE).
