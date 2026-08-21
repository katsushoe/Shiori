using System.Globalization;
using System.Resources;

namespace Shiori.Cli;

/// <summary>Provides localized CLI text from embedded resources.</summary>
internal static class CliText
{
    private static readonly ResourceManager Resources =
        new("Shiori.Cli.Resources.Messages", typeof(CliText).Assembly);

    internal static string Get(string name, CultureInfo? culture = null) =>
        Resources.GetString(name, culture ?? CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException($"Missing localization resource: {name}");

    internal static string Format(string name, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(name), arguments);
}
