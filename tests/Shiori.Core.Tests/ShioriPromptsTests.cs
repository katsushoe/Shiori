using Shiori.Cli.Server;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class ShioriPromptsTests
{
    [Fact]
    public void ServerInstructions_DescribePurposeWorkflowAndContentLimitation()
    {
        Assert.Contains("local-first file discovery", ShioriPrompts.ServerInstructions, StringComparison.Ordinal);
        Assert.Contains("workspace_list", ShioriPrompts.ServerInstructions, StringComparison.Ordinal);
        Assert.Contains("does not read or search file contents", ShioriPrompts.ServerInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public void GetGuide_DescribesSearchAdministrationAndSafety()
    {
        var guide = ShioriPrompts.GetGuide();

        Assert.Contains("Recommended workflow", guide, StringComparison.Ordinal);
        Assert.Contains("workspace_add", guide, StringComparison.Ordinal);
        Assert.Contains("Safety boundaries", guide, StringComparison.Ordinal);
        Assert.Contains("never searches inside file contents", guide, StringComparison.Ordinal);
    }
}
