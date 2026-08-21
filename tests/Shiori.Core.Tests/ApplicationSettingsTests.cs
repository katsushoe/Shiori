using Shiori.Cli;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class ApplicationSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsEnglish()
    {
        var result = ApplicationSettings.Load(_directory);

        Assert.Equal("en-US", result.Language);
    }

    [Fact]
    public void Load_WhenJapaneseIsConfigured_ReturnsJapanese()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, ApplicationSettings.FileName), "[general]\nlanguage=ja-JP\n");

        var result = ApplicationSettings.Load(_directory);

        Assert.Equal("ja-JP", result.Language);
    }

    [Fact]
    public void Load_WhenLanguageIsUnsupported_ThrowsInvalidDataException()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, ApplicationSettings.FileName), "[general]\nlanguage=invalid\n");

        var exception = Assert.Throws<InvalidDataException>(() => ApplicationSettings.Load(_directory));

        Assert.Contains("Unsupported language", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
