# macOS support

Shiori supports macOS 15 on Apple Silicon (`arm64`) and Intel (`x64`). Both
architectures run the Rust and .NET test suites, package build, Native ABI
loading, diagnostics, and an AST-search smoke test in GitHub Actions.

## Requirements

- .NET 10 runtime
- `ripgrep` on `PATH`
- A C# language server on `PATH` only when semantic C# navigation is required

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
bash scripts/publish-macos.sh 1.1.1
```

The package is framework-dependent and contains `libshiori_engine.dylib` beside
the managed executable so .NET can resolve the versioned Native ABI.
