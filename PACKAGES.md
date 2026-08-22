# PACKAGES.md Version
2026.08.17

# Change History

- 2026.08.17

# Shiori Package Inventory

This document is the source of truth for Shiori package references, their
purpose, sources, and update policy.

# Target Projects

| Project | Target | Reference system |
| :--- | :--- | :--- |
| `src/Shiori.Cli/Shiori.Cli.csproj` | `net10.0` | NuGet `PackageReference` |
| `tests/Shiori.Core.Tests/Shiori.Core.Tests.csproj` | `net10.0` | NuGet `PackageReference` |
| `native/shiori-engine/Cargo.toml` | Rust 2024 | Cargo dependencies |

# Package Sources

NuGet packages resolve from the user and machine sources selected by the .NET
SDK. Rust crates resolve from crates.io. Shiori does not require a private or
local package feed. Lock files and project manifests are the version source of
truth; credentials and authenticated feed URLs must not be committed.

# Direct Packages

## NuGet

| Project | Package | Version | Source | Purpose | Update policy |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Shiori.Cli` | `ModelContextProtocol.AspNetCore` | `2.0.0` | NuGet | Streamable HTTP MCP server | Review MCP compatibility before updating |
| `Shiori.Core.Tests` | `Microsoft.NET.Test.Sdk` | `18.0.1` | NuGet | .NET test host | Update with the supported SDK |
| `Shiori.Core.Tests` | `xunit` | `2.9.3` | NuGet | Test framework | Update with runner compatibility |
| `Shiori.Core.Tests` | `xunit.runner.visualstudio` | `3.1.4` | NuGet | Test discovery | Keep private and align with `xunit` |

## Cargo

| Package | Version | Purpose | Update policy |
| :--- | :--- | :--- | :--- |
| `ignore` | `0.4.33` | Gitignore-aware traversal | Update with indexing tests |
| `rusqlite` | `0.40.2` | Bundled SQLite file index | Verify schema and native packaging |
| `serde` / `serde_json` | `1.0.229` / `1.0.151` | ABI JSON serialization | Preserve ABI compatibility |
| `sha2` | `0.11.0` | Stable workspace identifiers | Update with workspace-ID tests |

# Transitive Packages

## NuGet

| Package | Resolved version | Main origin |
| :--- | :--- | :--- |
| `Microsoft.CodeCoverage` | `18.0.1` | `Microsoft.NET.Test.Sdk` |
| `Microsoft.Extensions.AI.Abstractions` | `10.8.3` | `ModelContextProtocol` |
| `Microsoft.Extensions.Caching.Abstractions` | `10.0.10` | MCP dependency graph |
| `Microsoft.Extensions.Configuration.Abstractions` | `10.0.10` | MCP dependency graph |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.10` | MCP dependency graph |
| `Microsoft.Extensions.Diagnostics.Abstractions` | `10.0.10` | MCP dependency graph |
| `Microsoft.Extensions.FileProviders.Abstractions` | `10.0.10` | MCP dependency graph |
| `Microsoft.Extensions.Hosting.Abstractions` | `10.0.10` | MCP dependency graph |
| `Microsoft.Extensions.Logging.Abstractions` | `10.0.10` | MCP dependency graph |
| `Microsoft.Extensions.Options` | `10.0.10` | MCP dependency graph |
| `Microsoft.Extensions.Primitives` | `10.0.10` | MCP dependency graph |
| `Microsoft.TestPlatform.ObjectModel` | `18.0.1` | `Microsoft.NET.Test.Sdk` |
| `Microsoft.TestPlatform.TestHost` | `18.0.1` | `Microsoft.NET.Test.Sdk` |
| `ModelContextProtocol` | `2.0.0` | `ModelContextProtocol.AspNetCore` |
| `ModelContextProtocol.Core` | `2.0.0` | `ModelContextProtocol.AspNetCore` |
| `Newtonsoft.Json` | `13.0.3` | .NET test platform |
| `xunit.abstractions` | `2.0.3` | `xunit` |
| `xunit.analyzers` | `1.18.0` | `xunit` |
| `xunit.assert` | `2.9.3` | `xunit` |
| `xunit.core` | `2.9.3` | `xunit` |
| `xunit.extensibility.core` | `2.9.3` | `xunit` |
| `xunit.extensibility.execution` | `2.9.3` | `xunit` |

## Cargo

Cargo's complete resolved transitive dependency list and checksums are recorded
in the tracked `native/shiori-engine/Cargo.lock`; `cargo tree` provides the
direct-origin hierarchy. The lock file is kept as the detailed list because the
same crate may resolve through several platform dependency paths.

Do not add a transitive dependency directly unless Shiori uses its public API
or must pin it for a documented compatibility or security reason.

# Update Rules

Update direct dependencies in their manifest, regenerate the relevant lock or
assets files, review licenses and security advisories, then run Rust and .NET
tests. MCP, SQLite, and test-runner updates require focused
compatibility checks. Keep `README.md`, `CONFIG.md`, and specifications aligned
when a dependency changes an external capability.

# Verification Commands

```powershell
dotnet list Shiori.slnx package --include-transitive
cargo tree --manifest-path native\shiori-engine\Cargo.toml
dotnet test tests\Shiori.Core.Tests\Shiori.Core.Tests.csproj --configuration Release
cargo test --manifest-path native\shiori-engine\Cargo.toml
```
