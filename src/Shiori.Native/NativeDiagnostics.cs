namespace Shiori.Native;

/// <summary>Contains diagnostics reported by the Rust native engine.</summary>
public sealed record NativeDiagnostics(
    uint AbiVersion,
    SqliteDiagnostics Sqlite);

/// <summary>Contains diagnostics for the bundled SQLite runtime.</summary>
public sealed record SqliteDiagnostics(string Version, string QuickCheck);
