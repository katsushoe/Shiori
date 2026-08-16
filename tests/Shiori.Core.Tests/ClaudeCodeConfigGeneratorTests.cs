using System.Text.Json;
using Shiori.Core.Integration;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class ClaudeCodeConfigGeneratorTests
{
    [Fact]
    public void Generate_returns_valid_streamable_http_project_configuration()
    {
        var json = ClaudeCodeConfigGenerator.Generate(41234, "shiori-local");
        using var document = JsonDocument.Parse(json);
        var server = document.RootElement
            .GetProperty("mcpServers")
            .GetProperty("shiori-local");

        Assert.Equal("http", server.GetProperty("type").GetString());
        Assert.Equal("http://127.0.0.1:41234/mcp", server.GetProperty("url").GetString());
        Assert.Equal(
            "Bearer ${SHIORI_MCP_TOKEN}",
            server.GetProperty("headers").GetProperty("Authorization").GetString());
        Assert.DoesNotContain("SHIORI_ALLOWED_WORKSPACES", json);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Generate_rejects_invalid_port(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClaudeCodeConfigGenerator.Generate(port));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad name")]
    [InlineData("../bad")]
    public void Generate_rejects_invalid_server_name(string name)
    {
        Assert.Throws<ArgumentException>(() => ClaudeCodeConfigGenerator.Generate(serverName: name));
    }
}
