using System.Globalization;

namespace Shiori.Cli;

/// <summary>Applies the configured application language to the current process.</summary>
internal static class ApplicationCulture
{
    internal static ApplicationSettings Apply(string? configDirectory = null)
    {
        var settings = ApplicationSettings.Load(configDirectory);
        var culture = CultureInfo.GetCultureInfo(settings.Language);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        return settings;
    }
}
