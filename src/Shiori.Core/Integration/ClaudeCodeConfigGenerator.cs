using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shiori.Core.Integration;

/// <summary>Generates a project-scoped Claude Code MCP configuration.</summary>
public static partial class ClaudeCodeConfigGenerator
{
    /// <summary>Generates `.mcp.json` content that starts the Shiori stdio bridge; no token is embedded.</summary>
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

        var configuration = new
        {
            mcpServers = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [serverName] = new
                {
                    type = "stdio",
                    command = "shiori",
                    args = new[] { "mcp", "--port", port.ToString(CultureInfo.InvariantCulture) },
                },
            },
        };
        return JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true });
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerNameRegex();
}
