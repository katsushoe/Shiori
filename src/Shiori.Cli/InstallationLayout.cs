namespace Shiori.Cli;

/// <summary>Resolves the standard Shiori installation directories.</summary>
internal static class InstallationLayout
{
    internal const string DataHomeVariable = "SHIORI_DATA_HOME";

    /// <summary>Aligns native and managed storage with the resolved data directory.</summary>
    internal static void ApplyDataDirectory()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataHomeVariable)))
        {
            Environment.SetEnvironmentVariable(DataHomeVariable, GetDataDirectory());
        }
    }

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

    /// <summary>Gets the configured data directory.</summary>
    internal static string GetDataDirectory(string? configuredDirectory = null, string? baseDirectory = null)
    {
        var dataDirectory = configuredDirectory ?? Environment.GetEnvironmentVariable(DataHomeVariable);
        return string.IsNullOrWhiteSpace(dataDirectory)
            ? GetDirectory("data", baseDirectory)
            : Path.GetFullPath(dataDirectory);
    }
}
