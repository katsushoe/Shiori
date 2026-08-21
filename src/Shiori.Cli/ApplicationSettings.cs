using Microsoft.Extensions.Configuration;

namespace Shiori.Cli;

/// <summary>Represents settings loaded from the Shiori configuration file.</summary>
internal sealed record ApplicationSettings(string Language)
{
    internal const string DefaultLanguage = "en-US";
    internal const string FileName = "shiori.ini";

    private static readonly HashSet<string> SupportedLanguages =
        new(StringComparer.OrdinalIgnoreCase) { DefaultLanguage, "ja-JP" };

    /// <summary>Loads and validates the settings for the current installation.</summary>
    internal static ApplicationSettings Load(string? configDirectory = null)
    {
        var directory = configDirectory ?? InstallationLayout.GetDirectory("config");
        var settingsPath = Path.Combine(directory, FileName);
        if (!File.Exists(settingsPath))
        {
            return new ApplicationSettings(DefaultLanguage);
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(directory)
            .AddIniFile(FileName, optional: true, reloadOnChange: false)
            .Build();
        var language = configuration["general:language"] ?? DefaultLanguage;
        if (!SupportedLanguages.Contains(language))
        {
            throw new InvalidDataException($"Unsupported language in {FileName}: {language}");
        }

        return new ApplicationSettings(
            SupportedLanguages.Single(value => string.Equals(value, language, StringComparison.OrdinalIgnoreCase)));
    }
}
