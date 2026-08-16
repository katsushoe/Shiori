using Shiori.Cli;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class InstallationLayoutTests
{
    [Fact]
    public void GetInstallRoot_WhenExecutableIsUnderBin_ReturnsParentDirectory()
    {
        var installRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = InstallationLayout.GetInstallRoot(Path.Combine(installRoot, "bin"));

        Assert.Equal(Path.GetFullPath(installRoot), result);
    }

    [Fact]
    public void GetDataDirectory_WhenConfigured_ReturnsConfiguredDirectory()
    {
        var configured = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = InstallationLayout.GetDataDirectory(configured, Path.Combine("ignored", "bin"));

        Assert.Equal(Path.GetFullPath(configured), result);
    }

    [Fact]
    public void GetDataDirectory_WhenExecutableIsUnderBin_ReturnsStandardDataDirectory()
    {
        var installRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = InstallationLayout.GetDataDirectory(string.Empty, Path.Combine(installRoot, "bin"));

        Assert.Equal(Path.Combine(Path.GetFullPath(installRoot), "data"), result);
    }
}
