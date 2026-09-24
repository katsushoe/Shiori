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

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsNoExcludePatterns()
    {
        var result = ApplicationSettings.Load(_directory);

        Assert.Empty(result.ExcludePatterns);
    }

    [Fact]
    public void Load_WhenExcludePatternsAreConfigured_ReturnsTrimmedPatterns()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, ApplicationSettings.FileName),
            "[index]\nexclude_patterns= generated/** ;;*.min.js\n");

        var result = ApplicationSettings.Load(_directory);

        Assert.Equal(["generated/**", "*.min.js"], result.ExcludePatterns);
    }

    [Fact]
    public void Initialize_WhenFileDoesNotExist_CreatesFileWithLanguage()
    {
        var created = ApplicationSettings.Initialize("ja-JP", _directory);

        Assert.True(created);
        Assert.Equal("ja-JP", ApplicationSettings.Load(_directory).Language);
    }

    [Fact]
    public void Initialize_WhenFileExists_KeepsExistingContent()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, ApplicationSettings.FileName);
        const string existing = "[general]\nlanguage=ja-JP\n\n[index]\nexclude_patterns=generated/**\n";
        File.WriteAllText(path, existing);

        var created = ApplicationSettings.Initialize("en-US", _directory);

        Assert.False(created);
        Assert.Equal(existing, File.ReadAllText(path));
    }

    [Fact]
    public void Initialize_WhenLanguageIsUnsupported_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ApplicationSettings.Initialize("fr-FR", _directory));
        Assert.False(File.Exists(Path.Combine(_directory, ApplicationSettings.FileName)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
