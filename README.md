# Shiori

[English](README.md) | [日本語](README.ja.md)

Shiori is a fast, local-first file search server for AI coding agents. Indexed
code search and semantic navigation are available as secondary capabilities.
The server exposes a single Streamable HTTP MCP endpoint and keeps independent
SQLite indexes for each workspace.

Product version: `1.1.1`.

## Getting Started

1. Install Shiori using one of the methods below.
2. Configure the MCP bearer token and allowed workspaces.
3. Build the initial index for each workspace.
4. Start the server and connect your AI coding agent.

## Installation

### Windows installer

Download `shiori-v1.1.1-win-x64-setup.exe` from the
[latest release](https://github.com/katsushoe/Shiori/releases/latest), run it,
and keep **Add Shiori to the current user's PATH** selected. The installer is
self-contained, installs only for the current user, and can be removed from
Windows **Installed apps**. Open a new terminal after installation.

```powershell
shiori doctor
```

### ZIP binary

Download `shiori-v1.1.1-win-x64.zip` from the latest release, verify the adjacent
SHA-256 file, extract it to a permanent directory, and add that directory to
your user `PATH`. The ZIP is self-contained and does not require a separate .NET
installation.

```powershell
$expected = (Get-Content .\shiori-v1.1.1-win-x64.zip.sha256).Split()[0]
$actual = (Get-FileHash .\shiori-v1.1.1-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw "Checksum mismatch" }
```

### Build from source

Install the .NET 10 SDK, Rust stable toolchain, Visual Studio 2022 C++ Build
Tools, Git, and optionally Inno Setup 6. Then clone and build:

```powershell
git clone https://github.com/katsushoe/Shiori.git
Set-Location Shiori
cargo build --release --manifest-path .\native\shiori-engine\Cargo.toml
dotnet restore .\Shiori.slnx
dotnet build .\Shiori.slnx --configuration Release --no-restore
dotnet test .\Shiori.slnx --configuration Release --no-build
.\scripts\Publish-Windows.ps1 -Version 1.1.1
```

The publish script writes the installer, ZIP, and checksum files to
`artifacts/`. Use `-SkipInstaller` when Inno Setup is not installed.

## Initial Configuration

Create a random token of at least 32 characters and list every directory that
the MCP server may access. Windows separates workspace paths with `;`.

```powershell
$env:SHIORI_MCP_TOKEN = ([guid]::NewGuid().ToString('N'))
$env:SHIORI_ALLOWED_WORKSPACES = 'F:\Projects\ProjectA;F:\Projects\ProjectB'
shiori doctor
```

Persist these values using a secure user-level environment configuration if
the server must survive terminal restarts. Registrations made by `workspace add`
do not grant MCP access. See [CONFIG.md](CONFIG.md) for every setting.

## Build the Initial Index

Build one independent index for each workspace before the first search:

```powershell
shiori index build --allow F:\Projects\ProjectA
shiori index build --allow F:\Projects\ProjectB
shiori index status --allow F:\Projects\ProjectA
```

Later `index build` calls are incremental. For MCP clients, `update_indexes`
updates selected or all allowed workspaces and returns after completion.

## Start and Connect

```powershell
shiori serve --port 39473
```

The MCP endpoint is `http://127.0.0.1:39473/mcp`. Generate client configuration
with `shiori config claude` or `shiori config codex`; both reference the token by
environment-variable name and never embed its value.

## Documentation

- [CLI command reference](COMMANDS.md)
- [Configuration reference](CONFIG.md)
- [MCP setup guide](MCP_SETUP.md)
- [Architecture](docs/architecture.md)
- [Specification (Japanese)](docs/specification.ja.md)
- [Multi-workspace coordination ADR](docs/adr/0002-multi-workspace-coordination.md)

## Security

Shiori listens only on loopback, requires bearer authentication for MCP, and
rejects access outside explicitly allowed workspace roots. Do not commit bearer
tokens or real environment settings.

## License

Shiori is licensed under the [MIT License](LICENSE).
