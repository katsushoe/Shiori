using Shiori.Cli.Server;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class ShioriToolsTests
{
    [Fact]
    public void GetVersion_WhenCalled_ReturnsFourPartVersion()
    {
        var result = ShioriTools.GetVersion();

        Assert.Equal("Shiori", result.Name);
        Assert.True(Version.TryParse(result.Version, out var version));
        Assert.NotNull(version);
        Assert.True(version.Revision >= 0);
    }
}
