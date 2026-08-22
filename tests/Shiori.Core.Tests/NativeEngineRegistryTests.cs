using Shiori.Cli.Server;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class NativeEngineRegistryTests
{
    [Fact]
    public void EmptyRegistry_AllowsListingAndDefaultResolution()
    {
        using var registry = new NativeEngineRegistry([]);

        Assert.Empty(registry.ListWorkspaces());
        Assert.Empty(registry.ResolveWorkspacePaths(null));
    }

    [Fact]
    public void EmptyRegistry_RejectsRequestedWorkspace()
    {
        using var registry = new NativeEngineRegistry([]);

        Assert.Throws<UnauthorizedAccessException>(() =>
            registry.ResolveWorkspacePaths([Path.GetTempPath()]));
    }
}
