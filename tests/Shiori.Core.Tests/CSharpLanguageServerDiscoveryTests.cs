using Shiori.Core.Lsp;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class CSharpLanguageServerDiscoveryTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"shiori-lsp-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Find_returns_explicit_server_before_path_candidates()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var configured = CreateExecutable("configured-lsp.exe");
        var candidate = CreateExecutable("csharp-ls.exe");

        var result = CSharpLanguageServerDiscovery.Find(
            configured,
            Path.GetDirectoryName(candidate),
            isWindows: true);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(configured), result.ExecutablePath);
        Assert.Equal(CSharpLanguageServerDiscovery.PathVariable, result.Source);
    }

    [Fact]
    public void Find_returns_first_path_candidate()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var candidate = CreateExecutable("csharp-ls.exe");

        var result = CSharpLanguageServerDiscovery.Find(
            searchPath: _temporaryDirectory,
            isWindows: true);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(candidate), result.ExecutablePath);
        Assert.Equal("PATH", result.Source);
    }

    [Fact]
    public void Find_returns_null_when_server_is_unavailable()
    {
        var result = CSharpLanguageServerDiscovery.Find(
            searchPath: _temporaryDirectory,
            isWindows: true);

        Assert.Null(result);
    }

    [Fact]
    public void Find_rejects_relative_configured_path()
    {
        var result = CSharpLanguageServerDiscovery.Find(
            "relative-lsp.exe",
            _temporaryDirectory,
            isWindows: true);

        Assert.Null(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private string CreateExecutable(string name)
    {
        var path = Path.Combine(_temporaryDirectory, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
