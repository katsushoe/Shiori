using System.Text.RegularExpressions;

namespace Shiori.Core.Integration;

/// <summary>Generates a Codex MCP server configuration.</summary>
public static partial class CodexConfigGenerator
{
    /// <summary>Generates TOML content without embedding the bearer-token value.</summary>
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
            url = "http://127.0.0.1:{port}/mcp"
            bearer_token_env_var = "SHIORI_MCP_TOKEN"
            """;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerNameRegex();
}
