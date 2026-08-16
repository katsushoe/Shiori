using System.Text.Json;
using Shiori.Core.Lsp;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class LspDefinitionServiceTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        $"shiori-definition-tests-{Guid.NewGuid():N}");
    private readonly LanguageServerDescriptor _descriptor = new(
        "csharp",
        Path.Combine(Path.GetTempPath(), "fake-lsp.exe"),
        "test");

    [Fact]
    public async Task FindAsync_converts_positions_and_normalizes_location()
    {
        var source = CreateFile("src/Source.cs");
        var target = CreateFile("src/Target.cs");
        var router = new FakeRouter(JsonSerializer.SerializeToElement(new
        {
            uri = new Uri(target).AbsoluteUri,
            range = new { start = new { line = 9, character = 4 } },
        }));

        var response = await LspDefinitionService.FindAsync(
            router, _workspace, source, 3, 7, _descriptor);

        Assert.True(response.Success);
        var location = Assert.Single(response.Locations);
        Assert.Equal("src/Target.cs", location.Path);
        Assert.Equal(10, location.Line);
        Assert.Equal(5, location.Column);
        Assert.Equal(2, router.Parameters.GetProperty("position").GetProperty("line").GetInt32());
        Assert.Equal(6, router.Parameters.GetProperty("position").GetProperty("character").GetInt32());
    }

    [Fact]
    public async Task FindAsync_supports_location_link_and_filters_external_targets()
    {
        var source = CreateFile("Source.cs");
        var target = CreateFile("Target.cs");
        var result = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                targetUri = new Uri(target).AbsoluteUri,
                targetSelectionRange = new { start = new { line = 1, character = 2 } },
            },
            new
            {
                uri = new Uri(Path.Combine(Path.GetTempPath(), "outside.cs")).AbsoluteUri,
                range = new { start = new { line = 0, character = 0 } },
            },
        });

        var response = await LspDefinitionService.FindAsync(
            new FakeRouter(result), _workspace, source, 1, 1, _descriptor);

        var location = Assert.Single(response.Locations);
        Assert.Equal("Target.cs", location.Path);
        Assert.Equal(2, location.Line);
        Assert.Equal(3, location.Column);
    }

    [Fact]
    public async Task FindAsync_returns_structured_unavailable_error()
    {
        var source = CreateFile("Source.cs");
        var router = new FakeRouter(new IOException("server stopped"));

        var response = await LspDefinitionService.FindAsync(
            router, _workspace, source, 1, 1, _descriptor);

        Assert.False(response.Success);
        Assert.Equal("LSP_UNAVAILABLE", response.Code);
        Assert.True(response.FallbackAvailable);
        Assert.Empty(response.Locations);
    }

    [Fact]
    public async Task NavigateAsync_requests_references_with_declarations_and_limit()
    {
        var source = CreateFile("Source.cs");
        var first = CreateFile("First.cs");
        var second = CreateFile("Second.cs");
        var router = new FakeRouter(JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                uri = new Uri(first).AbsoluteUri,
                range = new { start = new { line = 1, character = 1 } },
            },
            new
            {
                uri = new Uri(second).AbsoluteUri,
                range = new { start = new { line = 2, character = 2 } },
            },
        }));

        var response = await LspNavigationService.NavigateAsync(
            router, _workspace, source, 4, 5, "references", 1, _descriptor);

        Assert.True(response.Success);
        Assert.Equal("textDocument/references", router.Method);
        Assert.True(router.Parameters.GetProperty("context").GetProperty("includeDeclaration").GetBoolean());
        Assert.Single(response.Locations);
        Assert.Equal("First.cs", response.Locations[0].Path);
    }

    [Fact]
    public async Task NavigateAsync_requests_implementations_and_normalizes_location_link()
    {
        var source = CreateFile("Contract.cs");
        var implementation = CreateFile("Service.cs");
        var router = new FakeRouter(JsonSerializer.SerializeToElement(new
        {
            targetUri = new Uri(implementation).AbsoluteUri,
            targetSelectionRange = new { start = new { line = 7, character = 3 } },
        }));

        var response = await LspNavigationService.NavigateAsync(
            router, _workspace, source, 2, 4, "implementations", 20, _descriptor);

        Assert.True(response.Success);
        Assert.Equal("textDocument/implementation", router.Method);
        Assert.False(router.Parameters.TryGetProperty("context", out _));
        var location = Assert.Single(response.Locations);
        Assert.Equal("Service.cs", location.Path);
        Assert.Equal(8, location.Line);
        Assert.Equal(4, location.Column);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private string CreateFile(string relativePath)
    {
        var path = Path.Combine(_workspace, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private sealed class FakeRouter : ILspRequestRouter
    {
        private readonly JsonElement _result;
        private readonly Exception? _exception;

        internal FakeRouter(JsonElement result)
        {
            _result = result;
        }

        internal FakeRouter(Exception exception)
        {
            _exception = exception;
        }

        internal JsonElement Parameters { get; private set; }

        internal string? Method { get; private set; }

        public Task<JsonElement> SendRequestAsync(
            LanguageServerDescriptor descriptor,
            string workspace,
            string method,
            object? parameters,
            CancellationToken cancellationToken = default)
        {
            Method = method;
            Parameters = JsonSerializer.SerializeToElement(parameters);
            return _exception is null
                ? Task.FromResult(_result)
                : Task.FromException<JsonElement>(_exception);
        }
    }
}
