using Shiori.Cli;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class IndexCommandArgumentsTests
{
    [Theory]
    [InlineData("status")]
    [InlineData("build")]
    [InlineData("rebuild")]
    public void GetWorkspace_WithPositionalWorkspace_ReturnsWorkspace(string operation)
    {
        var result = IndexCommandArguments.GetWorkspace([operation, @"F:\workspace"]);

        Assert.Equal(@"F:\workspace", result);
    }

    [Fact]
    public void GetWorkspace_WithLegacyAllowOption_ReturnsWorkspace()
    {
        var result = IndexCommandArguments.GetWorkspace(["status", "--allow", @"F:\workspace"]);

        Assert.Equal(@"F:\workspace", result);
    }

    [Fact]
    public void GetWorkspace_WithoutWorkspace_ReturnsNull()
    {
        var result = IndexCommandArguments.GetWorkspace(["status"]);

        Assert.Null(result);
    }
}
