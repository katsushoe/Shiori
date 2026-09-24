using Microsoft.Extensions.Configuration;
using Shiori.Native;

namespace Shiori.Cli;

/// <summary>Represents settings loaded from the Shiori configuration file.</summary>
internal sealed record ApplicationSettings(string Language, IReadOnlyList<string> ExcludePatterns)
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
            return new ApplicationSettings(DefaultLanguage, []);
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

        var excludePatterns = (configuration["index:exclude_patterns"] ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new ApplicationSettings(
            SupportedLanguages.Single(value => string.Equals(value, language, StringComparison.OrdinalIgnoreCase)),
            excludePatterns);
    }

    /// <summary>Creates the settings file with the given language unless it already exists.</summary>
    /// <returns>True when the file was created; false when an existing file was kept.</returns>
    internal static bool Initialize(string language, string? configDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (!SupportedLanguages.Contains(language))
        {
            throw new ArgumentException($"Unsupported language: {language}", nameof(language));
        }

        var directory = configDirectory ?? InstallationLayout.GetDirectory("config");
        var settingsPath = Path.Combine(directory, FileName);
        if (File.Exists(settingsPath))
        {
            return false;
        }

        Directory.CreateDirectory(directory);
        var canonical = SupportedLanguages.Single(value => string.Equals(value, language, StringComparison.OrdinalIgnoreCase));
        try
        {
            using var stream = new FileStream(settingsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write($"[general]\nlanguage={canonical}\n");
        }
        catch (IOException) when (File.Exists(settingsPath))
        {
            return false;
        }

        return true;
    }

    /// <summary>Loads the native engine options for the current installation.</summary>
    internal static NativeEngineOptions LoadEngineOptions() =>
        new(WorkspaceRegistry.GetDataRoot(), Load().ExcludePatterns);
}
