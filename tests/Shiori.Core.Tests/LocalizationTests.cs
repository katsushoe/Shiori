using System.Globalization;
using Shiori.Cli;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class LocalizationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;

    [Fact]
    public void Get_WhenJapaneseCultureIsSelected_ReturnsJapaneseText()
    {
        var result = CliText.Get("UnknownCommand", CultureInfo.GetCultureInfo("ja-JP"));

        Assert.Equal("不明なコマンドです: {0}", result);
    }

    [Fact]
    public void Get_WhenIndexCompletes_ReturnsLocalizedCompletionText()
    {
        var result = CliText.Get("IndexComplete", CultureInfo.GetCultureInfo("ja-JP"));

        Assert.Equal("インデクス作成が終了しました：{0}（{1}ファイル）", result);
    }

    [Fact]
    public void Apply_WhenJapaneseIsConfigured_ChangesCurrentUiCulture()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, ApplicationSettings.FileName), "[general]\nlanguage=ja-JP\n");

        var settings = ApplicationCulture.Apply(_directory);

        Assert.Equal("ja-JP", settings.Language);
        Assert.Equal("ja-JP", CultureInfo.CurrentUICulture.Name);
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _originalCulture;
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
