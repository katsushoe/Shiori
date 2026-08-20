using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Shiori.Core.Logging;

/// <summary>Writes <see cref="ILogger"/> output to daily rolling files under a configured directory.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _writeLock = new();

    /// <summary>Creates a provider that writes log files under the given directory.</summary>
    public FileLoggerProvider(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    internal void Write(string line)
    {
        var fileName = $"shiori-{DateTimeOffset.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log";
        var path = Path.Combine(_directory, fileName);
        lock (_writeLock)
        {
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
