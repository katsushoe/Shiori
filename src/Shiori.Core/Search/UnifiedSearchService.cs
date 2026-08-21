using Shiori.Core.Engine;

namespace Shiori.Core.Search;

/// <summary>Executes query plans across local search providers.</summary>
public static class UnifiedSearchService
{
    private static readonly IGitMetadataProvider DefaultGitMetadataProvider = new GitMetadataProvider();

    /// <summary>Plans and executes a bounded unified search.</summary>
    public static async Task<UnifiedSearchResponse> SearchAsync(
        IShioriEngine engine,
        string query,
        string? path = null,
        int limit = 20,
        CancellationToken cancellationToken = default,
        IGitMetadataProvider? gitMetadataProvider = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);

        var plan = QueryPlanner.Plan(query);
        var tasks = plan.Providers
            .Select(provider => Task.Run(
                () => ExecuteProvider(engine, provider, plan.SearchQuery, path, limit),
                cancellationToken))
            .ToArray();
        var executions = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = executions
            .Where(execution => execution.Error is not null)
            .ToDictionary(
                execution => execution.Provider.ToString().ToLowerInvariant(),
                execution => execution.Error!,
                StringComparer.Ordinal);
        IReadOnlyDictionary<string, GitFileMetadata>? gitMetadata = null;
        try
        {
            gitMetadata = (gitMetadataProvider ?? DefaultGitMetadataProvider).GetMetadata(
                engine.GetWorkspaceInfo().Path,
                executions.SelectMany(execution => execution.Results).Select(result => result.Path));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }

        var results = SearchRanker.Rank(
            plan.SearchQuery,
            executions.SelectMany(execution => execution.Results),
            limit,
            gitMetadata);
        return new UnifiedSearchResponse(plan, results, errors);
    }

    private static ProviderExecution ExecuteProvider(
        IShioriEngine engine,
        SearchProvider provider,
        string query,
        string? path,
        int limit)
    {
        try
        {
            var results = provider switch
            {
                SearchProvider.File => engine.SearchFiles(query, limit)
                    .Select(result => FromSearchResult(result, "file")).ToArray(),
                SearchProvider.Symbol => engine.SearchSymbols(query, path: path, limit: limit)
                    .Select(FromSymbolResult).ToArray(),
                SearchProvider.Text => engine.SearchText(query, path: path, limit: limit)
                    .Select(result => FromSearchResult(result, "text")).ToArray(),
                _ => throw new ArgumentOutOfRangeException(nameof(provider)),
            };
            return new ProviderExecution(provider, results, null);
        }
        catch (Exception exception)
        {
            return new ProviderExecution(provider, [], exception.Message);
        }
    }

    private static UnifiedSearchResult FromSearchResult(SearchResult result, string provider) => new(
        result.Type,
        provider,
        result.Path,
        result.Line,
        result.Column,
        null,
        null,
        null,
        null,
        result.Snippet,
        null,
        [provider]);

    private static UnifiedSearchResult FromSymbolResult(SymbolSearchResult result) => new(
        result.Type,
        "symbol",
        result.Path,
        result.Line,
        result.Column,
        result.Name,
        result.Kind,
        result.Language,
        result.Score,
        result.Signature,
        result.QualifiedName,
        ["symbol"]);

    private sealed record ProviderExecution(
        SearchProvider Provider,
        IReadOnlyList<UnifiedSearchResult> Results,
        string? Error);
}
