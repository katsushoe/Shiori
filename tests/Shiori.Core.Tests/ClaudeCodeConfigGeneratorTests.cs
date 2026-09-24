using System.Text.Json;
using Shiori.Core.Integration;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class ClaudeCodeConfigGeneratorTests
{
    [Fact]
    public void Generate_returns_stdio_bridge_project_configuration()
    {
        var json = ClaudeCodeConfigGenerator.Generate(41234, "shiori-local");
        using var document = JsonDocument.Parse(json);
        var server = document.RootElement
            .GetProperty("mcpServers")
            .GetProperty("shiori-local");

        Assert.Equal("stdio", server.GetProperty("type").GetString());
        Assert.Equal("shiori", server.GetProperty("command").GetString());
        Assert.Equal(
            ["mcp", "--port", "41234"],
            server.GetProperty("args").EnumerateArray().Select(item => item.GetString()));
        Assert.False(server.TryGetProperty("headers", out _));
        Assert.DoesNotContain("SHIORI_", json, StringComparison.Ordinal);
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
