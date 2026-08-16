using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shiori.Core.Integration;

/// <summary>Generates a project-scoped Claude Code MCP configuration.</summary>
public static partial class ClaudeCodeConfigGenerator
{
    /// <summary>Generates `.mcp.json` content without embedding the bearer-token value.</summary>
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
                    type = "http",
                    url = $"http://127.0.0.1:{port}/mcp",
                    headers = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Authorization"] = "Bearer ${SHIORI_MCP_TOKEN}",
                    },
                },
            },
        };
        return JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true });
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerNameRegex();
}
