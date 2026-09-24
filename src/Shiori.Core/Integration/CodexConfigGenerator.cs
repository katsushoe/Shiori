using System.Text.RegularExpressions;

namespace Shiori.Core.Integration;

/// <summary>Generates a Codex MCP server configuration.</summary>
public static partial class CodexConfigGenerator
{
    /// <summary>Generates TOML content that starts the Shiori stdio bridge; no token is embedded.</summary>
    public static string Generate(int port = 39473, string serverName = "shiori")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        if (!ServerNameRegex().IsMatch(serverName))
        {
            throw new ArgumentException(
                "Server name may contain only letters, digits, underscores, and hyphens.",
                nameof(serverName));
        }

        return $"""
            [mcp_servers.{serverName}]
            command = "shiori"
            args = ["mcp", "--port", "{port}"]
            """;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerNameRegex();
}
