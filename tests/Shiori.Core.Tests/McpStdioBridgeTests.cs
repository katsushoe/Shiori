using System.Net;
using System.Text;
using System.Text.Json;
using Shiori.Cli.Server;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class McpStdioBridgeTests
{
    private const string Token = "test-token-0123456789abcdefghijklmnopqrstuv";

    [Fact]
    public async Task RelayAsync_WhenServerReturnsEventStream_WritesCompactMessageAndSendsBearerToken()
    {
        var handler = new RecordingHandler(_ => EventStream(
            "event: message\ndata: {\"jsonrpc\":\"2.0\",\ndata: \"id\":1,\"result\":{\"protocolVersion\":\"2025-06-18\"}}\n\n"));
        var output = await RelayAsync(handler, """{"jsonrpc":"2.0","id":1,"method":"initialize"}""");

        Assert.Equal(
            """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18"}}""" + "\n",
            output);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(Token, request.Headers.Authorization?.Parameter);
        Assert.Equal(new Uri("http://127.0.0.1:39473/mcp"), request.RequestUri);
    }

    [Fact]
    public async Task RelayAsync_AfterInitialize_SendsNegotiatedProtocolVersion()
    {
        var handler = new RecordingHandler(request => request.Headers.Contains("MCP-Protocol-Version")
            ? Json("""{"jsonrpc":"2.0","id":2,"result":{}}""")
            : Json("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18"}}"""));
        var bridge = new McpStdioBridge(CreateClient(handler), Token);

        await bridge.RelayAsync(
            new StringReader("""{"jsonrpc":"2.0","id":1,"method":"initialize"}"""),
            new StringWriter(),
            CancellationToken.None);
        await bridge.RelayAsync(
            new StringReader("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""),
            new StringWriter(),
            CancellationToken.None);

        Assert.Equal(["2025-06-18"], handler.Requests[1].Headers.GetValues("MCP-Protocol-Version"));
    }

    [Fact]
    public async Task RelayAsync_WhenNotificationIsAccepted_WritesNothing()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));

        var output = await RelayAsync(handler, """{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Empty(output);
    }

    [Fact]
    public async Task RelayAsync_WhenServerRejectsToken_WritesJsonRpcError()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var output = await RelayAsync(handler, """{"jsonrpc":"2.0","id":"a","method":"tools/list"}""");

        using var document = JsonDocument.Parse(output);
        Assert.Equal("a", document.RootElement.GetProperty("id").GetString());
        Assert.Contains("401", document.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task RelayAsync_WhenServerIsUnavailable_WritesJsonRpcError()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));

        var output = await RelayAsync(handler, """{"jsonrpc":"2.0","id":7,"method":"tools/list"}""");

        using var document = JsonDocument.Parse(output);
        Assert.Equal(7, document.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(-32000, document.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void ReadEventStream_WhenMultipleEvents_ReturnsEachPayload()
    {
        var messages = McpStdioBridge.ReadEventStream("data: {\"a\":1}\r\n\r\nid: 2\r\ndata: {\"b\":2}\r\n");

        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], messages);
    }

    private static async Task<string> RelayAsync(RecordingHandler handler, string input)
    {
        var output = new StringWriter();
        var bridge = new McpStdioBridge(CreateClient(handler), Token);
        await bridge.RelayAsync(new StringReader(input + "\n"), output, CancellationToken.None);
        return output.ToString();
    }

    private static HttpClient CreateClient(RecordingHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://127.0.0.1:39473/") };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage EventStream(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
