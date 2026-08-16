using Shiori.Core.Integration;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class CodexConfigGeneratorTests
{
    [Fact]
    public void Generate_returns_streamable_http_configuration()
    {
        var toml = CodexConfigGenerator.Generate(41234, "shiori-local");

        Assert.Contains("[mcp_servers.shiori-local]", toml, StringComparison.Ordinal);
        Assert.Contains("url = \"http://127.0.0.1:41234/mcp\"", toml, StringComparison.Ordinal);
        Assert.Contains(
            "bearer_token_env_var = \"SHIORI_MCP_TOKEN\"",
            toml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("SHIORI_ALLOWED_WORKSPACES", toml, StringComparison.Ordinal);
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
