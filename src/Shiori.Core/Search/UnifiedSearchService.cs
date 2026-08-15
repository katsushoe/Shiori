using Shiori.Core.Engine;

namespace Shiori.Core.Search;

/// <summary>Executes query plans across local search providers.</summary>
public static class UnifiedSearchService
{
    /// <summary>Plans and executes a bounded unified search.</summary>
    public static async Task<UnifiedSearchResponse> SearchAsync(
        IShioriEngine engine,
        string query,
        string? path = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
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
        var results = RoundRobin(executions, limit);
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
                SearchProvider.File => engine.SearchFiles(query, limit).Select(FromSearchResult).ToArray(),
                SearchProvider.Symbol => engine.SearchSymbols(query, path: path, limit: limit)
                    .Select(FromSymbolResult).ToArray(),
                SearchProvider.Text => engine.SearchText(query, path: path, limit: limit)
                    .Select(FromSearchResult).ToArray(),
                _ => throw new ArgumentOutOfRangeException(nameof(provider)),
            };
            return new ProviderExecution(provider, results, null);
        }
        catch (Exception exception)
        {
            return new ProviderExecution(provider, [], exception.Message);
        }
    }

    private static UnifiedSearchResult FromSearchResult(SearchResult result) => new(
        result.Type,
        result.Type,
        result.Path,
        result.Line,
        result.Column,
        null,
        null,
        null,
        null,
        result.Snippet);

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
        result.Signature);

    private static IReadOnlyList<UnifiedSearchResult> RoundRobin(
        IReadOnlyList<ProviderExecution> executions,
        int limit)
    {
        var results = new List<UnifiedSearchResult>(limit);
        for (var index = 0; results.Count < limit; index++)
        {
            var added = false;
            foreach (var execution in executions)
            {
                if (index < execution.Results.Count)
                {
                    results.Add(execution.Results[index]);
                    added = true;
                    if (results.Count == limit)
                    {
                        break;
                    }
                }
            }

            if (!added)
            {
                break;
            }
        }
        return results;
    }

    private sealed record ProviderExecution(
        SearchProvider Provider,
        IReadOnlyList<UnifiedSearchResult> Results,
        string? Error);
}
