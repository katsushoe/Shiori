# macOS support

**Formal macOS distribution is not planned.** Windows is the only officially
released and supported platform. macOS 15 on Apple Silicon (`arm64`) and
Intel (`x64`) is exercised in CI (test suites, package build, Native ABI
loading, diagnostics, and a file-search smoke test) for portability, and the
build steps below are provided for anyone building from source at their own
risk, but no official macOS package is published or supported.

## Requirements

- .NET 10 runtime

## Package

Use the archive matching the Mac architecture:

- Apple Silicon: `shiori-v<version>-osx-arm64.tar.gz`
- Intel: `shiori-v<version>-osx-x64.tar.gz`

Verify the adjacent `.sha256` file, extract the archive, and run:

```bash
shasum -a 256 -c shiori-v<version>-osx-<architecture>.tar.gz.sha256
./shiori doctor
```

When allowing multiple MCP workspaces, separate absolute paths in
`SHIORI_ALLOWED_WORKSPACES` with `:`. Codex configuration belongs in
`~/.codex/config.toml`.

To build a package on macOS, restore the .NET dependencies and invoke the
repository script. It detects the host architecture and writes the archive and
checksum under `artifacts/`.

```bash
dotnet restore
bash scripts/publish-macos.sh <version>
```

The package is framework-dependent and contains `libshiori_engine.dylib` beside
the managed executable so .NET can resolve the versioned Native ABI.
