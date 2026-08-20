using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Shiori.Core.Logging;

/// <summary>Structured log helpers for search-tool invocations (query, result count, elapsed time).</summary>
public static class SearchLogExtensions
{
    private static readonly EventId SearchSucceededEvent = new(1001, "SearchSucceeded");
    private static readonly EventId SearchPartialErrorEvent = new(1002, "SearchPartialError");
    private static readonly EventId SearchFailedEvent = new(1003, "SearchFailed");

    /// <summary>Logs a completed search: query, workspace, result count, and elapsed time.</summary>
    public static void LogSearchSucceeded(
        this ILogger logger,
        string tool,
        string query,
        string? workspace,
        int resultCount,
        double elapsedMilliseconds,
        [CallerFilePath] string sourceFile = "",
        [CallerLineNumber] int sourceLine = 0)
    {
        if (!logger.IsEnabled(LogLevel.Information)) return;
        var message = string.Create(CultureInfo.InvariantCulture,
            $"[Search][{tool}] query=\"{query}\" workspace={workspace ?? "(all)"} results={resultCount} elapsedMs={elapsedMilliseconds:F1}");
        logger.Log(
            LogLevel.Information,
            SearchSucceededEvent,
            new SearchLogState(message, sourceFile, sourceLine),
            null,
            static (state, _) => state.Message);
    }

    /// <summary>Logs recoverable per-provider or per-workspace errors from an otherwise successful search.</summary>
    public static void LogSearchPartialErrors(
        this ILogger logger,
        string tool,
        string query,
        IReadOnlyCollection<string> errorDetails,
        [CallerFilePath] string sourceFile = "",
        [CallerLineNumber] int sourceLine = 0)
    {
        if (errorDetails.Count == 0 || !logger.IsEnabled(LogLevel.Warning)) return;
        var message = string.Create(CultureInfo.InvariantCulture,
            $"[Search][{tool}] query=\"{query}\" partial errors: {string.Join(" | ", errorDetails)}");
        logger.Log(
            LogLevel.Warning,
            SearchPartialErrorEvent,
            new SearchLogState(message, sourceFile, sourceLine),
            null,
            static (state, _) => state.Message);
    }

    /// <summary>Logs a failed search: query, workspace, elapsed time, and the causing exception.</summary>
    public static void LogSearchFailed(
        this ILogger logger,
        string tool,
        string query,
        string? workspace,
        double elapsedMilliseconds,
        Exception exception,
        [CallerFilePath] string sourceFile = "",
        [CallerLineNumber] int sourceLine = 0)
    {
        var message = string.Create(CultureInfo.InvariantCulture,
            $"[Search][{tool}] query=\"{query}\" workspace={workspace ?? "(all)"} elapsedMs={elapsedMilliseconds:F1} failed");
        logger.Log(
            LogLevel.Error,
            SearchFailedEvent,
            new SearchLogState(message, sourceFile, sourceLine),
            exception,
            static (state, _) => state.Message);
    }
}
