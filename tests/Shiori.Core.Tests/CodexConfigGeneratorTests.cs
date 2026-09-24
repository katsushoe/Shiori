using Shiori.Core.Integration;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class CodexConfigGeneratorTests
{
    [Fact]
    public void Generate_returns_stdio_bridge_configuration()
    {
        var toml = CodexConfigGenerator.Generate(41234, "shiori-local");

        Assert.Contains("[mcp_servers.shiori-local]", toml, StringComparison.Ordinal);
        Assert.Contains("command = \"shiori\"", toml, StringComparison.Ordinal);
        Assert.Contains("args = [\"mcp\", \"--port\", \"41234\"]", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer_token", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("SHIORI_", toml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Generate_rejects_invalid_port(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CodexConfigGenerator.Generate(port));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad name")]
    [InlineData("../bad")]
    public void Generate_rejects_invalid_server_name(string name)
    {
        Assert.Throws<ArgumentException>(() => CodexConfigGenerator.Generate(serverName: name));
    }
}
