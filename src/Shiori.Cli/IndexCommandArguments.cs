namespace Shiori.Cli;

/// <summary>Parses workspace selection for index commands.</summary>
internal static class IndexCommandArguments
{
    internal static string? GetWorkspace(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count >= 2 && !arguments[1].StartsWith("-", StringComparison.Ordinal))
        {
            return arguments[1];
        }

        for (var index = 1; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], "--allow", StringComparison.Ordinal))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
