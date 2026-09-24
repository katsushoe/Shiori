namespace Shiori.Native;

/// <summary>Configures where the native engine stores data and which paths indexing skips.</summary>
/// <param name="DataRoot">The directory that holds the unified Shiori database.</param>
/// <param name="ExcludePatterns">Gitignore-style patterns excluded from indexing.</param>
public sealed record NativeEngineOptions(string DataRoot, IReadOnlyList<string> ExcludePatterns);
