namespace Shiori.Core.Engine;

/// <summary>Represents a structured code-search result.</summary>
public sealed record SearchResult(string Type, string Path, long? Line, string? Snippet, long? Column = null);
