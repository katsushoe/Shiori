using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shiori.Cli.Server;

/// <summary>Relays newline-delimited stdio JSON-RPC messages to the local Streamable HTTP MCP endpoint.</summary>
internal sealed class McpStdioBridge
{
    private const string ProtocolVersionHeader = "MCP-Protocol-Version";
    private const int BridgeErrorCode = -32000;

    private readonly HttpClient _client;
    private readonly string _token;
    private readonly SemaphoreSlim _outputLock = new(1, 1);
    private volatile string? _protocolVersion;

    /// <summary>Initializes a bridge whose client targets the Shiori server origin.</summary>
    internal McpStdioBridge(HttpClient client, string token)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _client = client;
        _token = token;
    }

    /// <summary>Runs the bridge for the local server port using the protected MCP token.</summary>
    internal static async Task<int> RunAsync(int port, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromMinutes(5),
        };
        var bridge = new McpStdioBridge(client, new McpTokenStore().GetOrCreate());
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var input = new StreamReader(Console.OpenStandardInput(), encoding);
        await using var output = new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true };
        await bridge.RelayAsync(input, output, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    /// <summary>Relays each input line until the input closes, allowing requests to overlap.</summary>
    internal async Task RelayAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        var pending = new List<Task>();
        while (await input.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                pending.Add(RelayMessageAsync(line, output, cancellationToken));
            }
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
    }

    private async Task RelayMessageAsync(string message, TextWriter output, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> responses;
        try
        {
            responses = await SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            responses = CreateErrorResponse(message, $"Shiori MCP server request failed: {exception.Message}");
        }

        foreach (var response in responses)
        {
            await WriteAsync(output, response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<string>> SendAsync(string message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "mcp")
        {
            Content = new StringContent(message, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (_protocolVersion is { } protocolVersion)
        {
            request.Headers.Add(ProtocolVersionHeader, protocolVersion);
        }

        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return CreateErrorResponse(
                message,
                $"Shiori MCP server returned HTTP {(int)response.StatusCode}.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var messages = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? ReadEventStream(body)
            : string.IsNullOrWhiteSpace(body) ? [] : [body];
        var compact = messages.Select(Compact).ToArray();
        foreach (var item in compact)
        {
            CaptureProtocolVersion(item);
        }

        return compact;
    }

    /// <summary>Extracts the data payload of each server-sent event.</summary>
    internal static IReadOnlyList<string> ReadEventStream(string body)
    {
        var messages = new List<string>();
        var data = new StringBuilder();
        foreach (var line in body.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.Length == 0)
            {
                Flush(messages, data);
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line.AsSpan(5).TrimStart(' '));
            }
        }

        Flush(messages, data);
        return messages;
    }

    /// <summary>Creates a JSON-RPC error for a request, or nothing for a notification.</summary>
    internal static IReadOnlyList<string> CreateErrorResponse(string message, string error)
    {
        JsonNode? id;
        try
        {
            id = JsonNode.Parse(message) is JsonObject request ? request["id"]?.DeepClone() : null;
        }
        catch (JsonException)
        {
            id = null;
        }

        if (id is null)
        {
            return [];
        }

        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = BridgeErrorCode, ["message"] = error },
        };
        return [response.ToJsonString()];
    }

    private static void Flush(List<string> messages, StringBuilder data)
    {
        if (data.Length > 0)
        {
            messages.Add(data.ToString());
            data.Clear();
        }
    }

    private static string Compact(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private void CaptureProtocolVersion(string message)
    {
        if (JsonNode.Parse(message) is JsonObject response &&
            response["result"] is JsonObject result &&
            result["protocolVersion"] is JsonValue value &&
            value.TryGetValue<string>(out var version))
        {
            _protocolVersion = version;
        }
    }

    private async Task WriteAsync(TextWriter output, string message, CancellationToken cancellationToken)
    {
        await _outputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await output.WriteAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
            await output.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _outputLock.Release();
        }
    }
}
