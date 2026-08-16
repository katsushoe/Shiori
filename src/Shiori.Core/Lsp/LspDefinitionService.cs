using System.Text.Json;

namespace Shiori.Core.Lsp;

/// <summary>Resolves semantic-navigation actions through an available language server.</summary>
public static class LspNavigationService
{
    /// <summary>Runs a supported navigation action for a one-based source position.</summary>
    public static async Task<NavigationResponse> NavigateAsync(
        ILspRequestRouter router,
        string workspace,
        string file,
        int line,
        int column,
        string action,
        int limit = 20,
        LanguageServerDescriptor? descriptor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        var method = action switch
        {
            "definition" => "textDocument/definition",
            "references" => "textDocument/references",
            "implementations" => "textDocument/implementation",
            _ => throw new ArgumentException($"Unsupported navigation action: {action}", nameof(action)),
        };

        var canonicalWorkspace = Path.GetFullPath(workspace);
        var sourcePath = ResolveWorkspacePath(canonicalWorkspace, file);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source file is unavailable.", sourcePath);
        }

        descriptor ??= CSharpLanguageServerDiscovery.Find();
        if (descriptor is null)
        {
            return Unavailable("C# language server is unavailable.");
        }

        try
        {
            var result = await router.SendRequestAsync(
                descriptor,
                canonicalWorkspace,
                method,
                CreateParameters(action, sourcePath, line, column),
                cancellationToken).ConfigureAwait(false);
            return new NavigationResponse(
                true,
                null,
                null,
                false,
                ParseLocations(result, canonicalWorkspace, limit));
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException
            or LspProtocolException or InvalidOperationException or ObjectDisposedException)
        {
            return Unavailable(exception.Message);
        }
    }

    private static object CreateParameters(string action, string sourcePath, int line, int column)
    {
        var textDocument = new { uri = new Uri(sourcePath).AbsoluteUri };
        var position = new { line = line - 1, character = column - 1 };
        return action == "references"
            ? new { textDocument, position, context = new { includeDeclaration = true } }
            : (object)new { textDocument, position };
    }

    private static IReadOnlyList<NavigationLocation> ParseLocations(
        JsonElement result,
        string workspace,
        int limit)
    {
        if (result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return [];
        IEnumerable<JsonElement> locations = result.ValueKind == JsonValueKind.Array
            ? result.EnumerateArray().ToArray()
            : [result];
        return locations
            .Select(location => ParseLocation(location, workspace))
            .Where(location => location is not null)
            .Cast<NavigationLocation>()
            .Distinct()
            .Take(limit)
            .ToArray();
    }

    private static NavigationLocation? ParseLocation(JsonElement location, string workspace)
    {
        var uriName = location.TryGetProperty("targetUri", out var targetUri) ? targetUri :
            location.TryGetProperty("uri", out var uri) ? uri : default;
        if (uriName.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(uriName.GetString(), UriKind.Absolute, out var parsedUri)
            || !parsedUri.IsFile)
        {
            return null;
        }

        var path = Path.GetFullPath(parsedUri.LocalPath);
        if (!IsInsideWorkspace(workspace, path)) return null;
        var range = location.TryGetProperty("targetSelectionRange", out var selectionRange)
            ? selectionRange
            : location.TryGetProperty("range", out var normalRange) ? normalRange : default;
        if (range.ValueKind != JsonValueKind.Object
            || !range.TryGetProperty("start", out var start)
            || !start.TryGetProperty("line", out var lineElement)
            || !start.TryGetProperty("character", out var columnElement)
            || !lineElement.TryGetInt32(out var line)
            || !columnElement.TryGetInt32(out var column))
        {
            return null;
        }

        return new NavigationLocation(
            Path.GetRelativePath(workspace, path).Replace(Path.DirectorySeparatorChar, '/'),
            line + 1,
            column + 1);
    }

    private static string ResolveWorkspacePath(string workspace, string file)
    {
        var path = Path.GetFullPath(Path.IsPathFullyQualified(file) ? file : Path.Combine(workspace, file));
        if (!IsInsideWorkspace(workspace, path))
        {
            throw new UnauthorizedAccessException("Source file is outside the workspace.");
        }

        return path;
    }

    private static bool IsInsideWorkspace(string workspace, string path)
    {
        var relative = Path.GetRelativePath(workspace, path);
        return !Path.IsPathFullyQualified(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static NavigationResponse Unavailable(string message) =>
        new(false, "LSP_UNAVAILABLE", message, true, []);
}
