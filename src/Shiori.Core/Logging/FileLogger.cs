using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Shiori.Core.Logging;

/// <summary>Formats and writes one <see cref="ILogger"/> category to the house log-line standard.</summary>
internal sealed class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var (sourceFile, sourceLine) = state is ISourceLocatedLogState located
            ? (Path.GetFileName(located.SourceFile), located.SourceLine)
            : (categoryName, 0);
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        var location = sourceLine > 0 ? $"{sourceFile}（{sourceLine.ToString(CultureInfo.InvariantCulture)}）" : sourceFile;
        var line = $"{timestamp} {LevelTag(logLevel)} {message} {location}";
        if (exception is not null)
        {
            line += $" | Exception: {exception}";
        }

        provider.Write(line);
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "[D]",
        LogLevel.Information => "[I]",
        LogLevel.Warning => "[W]",
        LogLevel.Error or LogLevel.Critical => "[E]",
        _ => "[I]",
    };
}
