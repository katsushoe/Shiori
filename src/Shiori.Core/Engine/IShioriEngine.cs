namespace Shiori.Core.Engine;

/// <summary>Defines the managed-to-native search engine boundary.</summary>
public interface IShioriEngine : IDisposable
{
    /// <summary>Gets the engine ABI version.</summary>
    uint AbiVersion { get; }

    /// <summary>Searches file names and paths in the allowed workspace.</summary>
    IReadOnlyList<SearchResult> SearchFiles(string query, int limit = 20);
}
