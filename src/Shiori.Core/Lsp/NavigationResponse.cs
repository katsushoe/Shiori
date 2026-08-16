namespace Shiori.Core.Lsp;

/// <summary>Contains semantic-navigation results or a structured availability error.</summary>
public sealed record NavigationResponse(
    bool Success,
    string? Code,
    string? Message,
    bool FallbackAvailable,
    IReadOnlyList<NavigationLocation> Locations);
