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

    [Fact]
    public void LiveAccessBoundary_AllowsAndDisallowsWorkspace()
    {
        var workspace = Path.GetFullPath(Path.GetTempPath());
        using var registry = new NativeEngineRegistry([]);

        registry.AllowWorkspace(workspace);
        Assert.Equal([workspace], registry.ResolveWorkspacePaths(null));

        registry.DisallowWorkspace(workspace);
        Assert.Empty(registry.ResolveWorkspacePaths(null));
        Assert.Throws<UnauthorizedAccessException>(() =>
            registry.ResolveWorkspacePaths([workspace]));
    }
}
