using System.Text.Json;
using Shiori.Core.Lsp;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class LspServerManagerTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        $"shiori-lsp-manager-tests-{Guid.NewGuid():N}");
    private readonly LanguageServerDescriptor _descriptor = new(
        "csharp",
        Path.Combine(Path.GetTempPath(), "fake-lsp.exe"),
        "test");

    [Fact]
    public async Task SendRequestAsync_lazily_starts_and_reuses_workspace_connection()
    {
        Directory.CreateDirectory(_workspace);
        var connection = new FakeConnection();
        var factory = new FakeFactory(connection);
        await using var manager = new LspServerManager(factory);

        await manager.SendRequestAsync(_descriptor, _workspace, "first", null);
        await manager.SendRequestAsync(_descriptor, _workspace, "second", null);

        Assert.Equal(1, factory.StartCount);
        Assert.Equal(2, connection.RequestCount);
    }

    [Fact]
    public async Task SendRequestAsync_restarts_once_after_transport_failure()
    {
        Directory.CreateDirectory(_workspace);
        var failed = new FakeConnection { FailRequest = true };
        var recovered = new FakeConnection();
        var factory = new FakeFactory(failed, recovered);
        await using var manager = new LspServerManager(factory);

        var result = await manager.SendRequestAsync(
            _descriptor,
            _workspace,
            "textDocument/definition",
            null);

        Assert.Equal("ok", result.GetString());
        Assert.Equal(2, factory.StartCount);
        Assert.True(failed.Disposed);
    }

    [Fact]
    public async Task DisposeAsync_stops_started_connections()
    {
        Directory.CreateDirectory(_workspace);
        var connection = new FakeConnection();
        var manager = new LspServerManager(new FakeFactory(connection));
        await manager.SendRequestAsync(_descriptor, _workspace, "initialize", null);

        await manager.DisposeAsync();

        Assert.True(connection.Disposed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private sealed class FakeFactory(params FakeConnection[] connections) : ILspServerConnectionFactory
    {
        private readonly Queue<FakeConnection> _connections = new(connections);

        internal int StartCount { get; private set; }

        public Task<ILspServerConnection> StartAsync(
            LanguageServerDescriptor descriptor,
            string workspace,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.FromResult<ILspServerConnection>(_connections.Dequeue());
        }
    }

    private sealed class FakeConnection : ILspServerConnection
    {
        internal bool Disposed { get; private set; }

        internal bool FailRequest { get; init; }

        internal int RequestCount { get; private set; }

        public bool IsAlive => !Disposed;

        public Task<JsonElement> SendRequestAsync(
            string method,
            object? parameters,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return FailRequest
                ? Task.FromException<JsonElement>(new IOException("connection failed"))
                : Task.FromResult(JsonSerializer.SerializeToElement("ok"));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
