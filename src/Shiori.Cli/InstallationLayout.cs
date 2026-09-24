namespace Shiori.Cli;

/// <summary>Resolves the standard Shiori installation directories.</summary>
internal static class InstallationLayout
{
    /// <summary>Gets the installation root for the current executable layout.</summary>
    internal static string GetInstallRoot(string? baseDirectory = null)
    {
        var applicationDirectory = new DirectoryInfo(
            Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory));
        if (string.Equals(applicationDirectory.Name, "bin", StringComparison.OrdinalIgnoreCase) &&
            applicationDirectory.Parent is not null)
        {
            return applicationDirectory.Parent.FullName;
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            throw new InvalidOperationException("The local application data directory is unavailable.");
        }

        return Path.Combine(localData, "Shiori");
    }

    /// <summary>Gets a named directory under the installation root.</summary>
    internal static string GetDirectory(string name, string? baseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Path.Combine(GetInstallRoot(baseDirectory), name);
    }

    /// <summary>Gets the data directory under the installation root.</summary>
    internal static string GetDataDirectory(string? baseDirectory = null) =>
        GetDirectory("data", baseDirectory);
}
