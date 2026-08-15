using System.Text.RegularExpressions;

namespace Shiori.Core.Search;

/// <summary>Selects local search providers from a user or agent query.</summary>
public static partial class QueryPlanner
{
    private static readonly SearchProvider[] SymbolProviders =
        [SearchProvider.Symbol, SearchProvider.File, SearchProvider.Text];
    private static readonly SearchProvider[] NavigationFallbackProviders =
        [SearchProvider.Symbol, SearchProvider.Text];
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "callers", "calls", "classes", "derived", "find", "implementations", "implements",
        "references", "to", "usage", "usages", "where",
    };

    /// <summary>Builds a bounded, deterministic search plan.</summary>
    public static SearchPlan Plan(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var original = query.Trim();

        var quoted = QuotedPhraseRegex().Match(original);
        if (quoted.Success)
        {
            return Create(original, quoted.Groups[1].Value, SearchIntent.Text,
                [SearchProvider.Text], "quoted_phrase");
        }

        if (ContainsAny(original, "呼び出", "参照", "使われ", "references", "callers", "usages"))
        {
            return Create(original, ExtractIdentifier(original), SearchIntent.References,
                NavigationFallbackProviders, "reference_intent_lsp_fallback");
        }

        if (ContainsAny(original, "継承", "実装", "派生", "implementations", "implements", "derived"))
        {
            return Create(original, ExtractIdentifier(original), SearchIntent.Implementations,
                NavigationFallbackProviders, "implementation_intent_lsp_fallback");
        }

        if (LooksLikeFile(original))
        {
            return Create(original, original.Trim('*'), SearchIntent.File,
                [SearchProvider.File], "file_name_or_path");
        }

        if (ContainsAny(original, "どこ", "where") && TryExtractIdentifier(original, out var located))
        {
            return Create(original, located, SearchIntent.Symbol,
                [SearchProvider.Symbol, SearchProvider.File], "symbol_location_intent");
        }

        if (IdentifierRegex().IsMatch(original))
        {
            return Create(original, original, SearchIntent.Symbol, SymbolProviders, "code_identifier");
        }

        return Create(original, original, SearchIntent.Text, [SearchProvider.Text], "natural_language_or_code_text");
    }

    private static SearchPlan Create(
        string original,
        string searchQuery,
        SearchIntent intent,
        IReadOnlyList<SearchProvider> providers,
        string reason) => new(original, searchQuery, intent, providers, reason);

    private static bool ContainsAny(string query, params string[] values) =>
        values.Any(value => query.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeFile(string query) =>
        query.Contains('/') || query.Contains('\\') || FileNameRegex().IsMatch(query.Trim('*'));

    private static string ExtractIdentifier(string query) =>
        TryExtractIdentifier(query, out var identifier) ? identifier : query;

    private static bool TryExtractIdentifier(string query, out string identifier)
    {
        identifier = IdentifierTokenRegex()
            .Matches(query)
            .Select(match => match.Value)
            .Where(value => !StopWords.Contains(value))
            .OrderByDescending(value => value.Length)
            .FirstOrDefault() ?? string.Empty;
        return identifier.Length > 0;
    }

    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*(?:(?:\\.|::)[A-Za-z_$][A-Za-z0-9_$]*)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("[A-Za-z_$][A-Za-z0-9_$]*", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierTokenRegex();

    [GeneratedRegex("^[^\\s]+\\.[A-Za-z0-9]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNameRegex();

    [GeneratedRegex("[\"“]([^\"”]+)[\"”]", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedPhraseRegex();
}
