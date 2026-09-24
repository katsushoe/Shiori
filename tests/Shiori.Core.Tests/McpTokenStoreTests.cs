using System.Security.Cryptography;
using Shiori.Cli;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class McpTokenStoreTests : IDisposable
{
    private readonly string _configDirectory =
        Path.Combine(Path.GetTempPath(), "shiori-token-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryRead_WhenFileIsMissing_ReturnsNull()
    {
        var store = new McpTokenStore(_configDirectory);

        Assert.Null(store.TryRead());
    }

    [Fact]
    public void GetOrCreate_WhenFileIsMissing_PersistsEncryptedRandomToken()
    {
        var store = new McpTokenStore(_configDirectory);

        var token = store.GetOrCreate();

        Assert.True(token.Length >= 32);
        Assert.Equal(token, new McpTokenStore(_configDirectory).TryRead());
        Assert.DoesNotContain(token, File.ReadAllText(store.FilePath), StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(_configDirectory));
    }

    [Fact]
    public void GetOrCreate_WhenTokenExists_ReturnsSameToken()
    {
        var first = new McpTokenStore(_configDirectory).GetOrCreate();

        var second = new McpTokenStore(_configDirectory).GetOrCreate();

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetOrCreate_WhenStoresAreSeparate_CreatesDifferentTokens()
    {
        var first = new McpTokenStore(_configDirectory).GetOrCreate();

        var second = new McpTokenStore(Path.Combine(_configDirectory, "other")).GetOrCreate();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TryRead_WhenFileIsNotProtectedData_ThrowsCryptographicException()
    {
        var store = new McpTokenStore(_configDirectory);
        Directory.CreateDirectory(_configDirectory);
        File.WriteAllText(store.FilePath, "plain-text-token");

        Assert.ThrowsAny<CryptographicException>(() => store.TryRead());
    }

    public void Dispose()
    {
        if (Directory.Exists(_configDirectory))
        {
            Directory.Delete(_configDirectory, recursive: true);
        }
    }
}
