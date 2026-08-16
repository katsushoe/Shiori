using System.Text;
using System.Text.Json;
using Shiori.Core.Lsp;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class LspJsonRpcTransportTests
{
    [Fact]
    public async Task SendRequestAsync_writes_frame_and_returns_matching_result()
    {
        await using var input = ResponseStream("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"uri\":\"file:///result.cs\"}}");
        await using var output = new MemoryStream();
        await using var transport = new LspJsonRpcTransport(input, output);

        var result = await transport.SendRequestAsync(
            "textDocument/definition",
            new { textDocument = new { uri = "file:///source.cs" } });

        Assert.Equal("file:///result.cs", result.GetProperty("uri").GetString());
        var request = ReadPayload(output);
        Assert.Equal("2.0", request.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, request.GetProperty("id").GetInt64());
        Assert.Equal("textDocument/definition", request.GetProperty("method").GetString());
    }

    [Fact]
    public async Task SendRequestAsync_throws_protocol_exception_for_error_response()
    {
        await using var input = ResponseStream(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32601,\"message\":\"missing\"}}");
        await using var output = new MemoryStream();
        await using var transport = new LspJsonRpcTransport(input, output);

        var exception = await Assert.ThrowsAsync<LspProtocolException>(
            () => transport.SendRequestAsync("missing", null));

        Assert.Contains("-32601", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendRequestAsync_rejects_oversized_content_length()
    {
        var header = Encoding.ASCII.GetBytes("Content-Length: 16777217\r\n\r\n");
        await using var input = new MemoryStream(header);
        await using var output = new MemoryStream();
        await using var transport = new LspJsonRpcTransport(input, output);

        await Assert.ThrowsAsync<LspProtocolException>(
            () => transport.SendRequestAsync("initialize", null));
    }

    [Fact]
    public async Task SendNotificationAsync_writes_frame_without_request_id()
    {
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();
        await using var transport = new LspJsonRpcTransport(input, output);

        await transport.SendNotificationAsync("initialized", new { });

        var notification = ReadPayload(output);
        Assert.Equal("initialized", notification.GetProperty("method").GetString());
        Assert.False(notification.TryGetProperty("id", out _));
    }

    private static MemoryStream ResponseStream(string json)
    {
        var content = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {content.Length}\r\n\r\n");
        return new MemoryStream([.. header, .. content]);
    }

    private static JsonElement ReadPayload(MemoryStream stream)
    {
        var frame = Encoding.UTF8.GetString(stream.ToArray());
        var separator = frame.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separator >= 0);
        using var document = JsonDocument.Parse(frame[(separator + 4)..]);
        return document.RootElement.Clone();
    }
}
