using Shiori.Cli.Server;
using System.Reflection;
using ModelContextProtocol.Server;
using Shiori.Core.Integration;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class ShioriToolsTests
{
    [Fact]
    public void ToolDeclarations_ContainCliEquivalentOperations()
    {
        var names = typeof(ShioriTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            [
                "config_claude", "config_codex", "doctor", "get_version", "index_build",
                "index_rebuild", "index_status", "search_files", "workspace_add",
                "workspace_list", "workspace_remove",
            ],
            names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void GetVersion_WhenCalled_ReturnsFourPartVersion()
    {
        var result = ShioriTools.GetVersion();

        Assert.Equal("Shiori", result.Name);
        Assert.True(Version.TryParse(result.Version, out var version));
        Assert.NotNull(version);
        Assert.True(version.Revision >= 0);
    }

    [Fact]
    public void ConfigTools_WhenCalled_MatchCliGenerators()
    {
        Assert.Equal(
            ClaudeCodeConfigGenerator.Generate(41234, "test-server"),
            ShioriTools.GenerateClaudeConfig(41234, "test-server"));
        Assert.Equal(
            CodexConfigGenerator.Generate(41234, "test-server"),
            ShioriTools.GenerateCodexConfig(41234, "test-server"));
    }

    [Fact]
    public void WindowsTerminalIndexLauncher_WhenCreated_PreservesExecutableAndWorkspaceArguments()
    {
        var startInfo = WindowsTerminalIndexLauncher.CreateStartInfo(
            @"C:\Users\test\AppData\Local\Microsoft\WindowsApps\wt.exe",
            @"C:\Program Files\Shiori\shiori.exe",
            @"F:\folder with spaces");

        Assert.Equal(
            @"C:\Users\test\AppData\Local\Microsoft\WindowsApps\wt.exe",
            startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(
            [
                "new-tab", "--title", @"Shiori index: F:\folder with spaces",
                @"C:\Program Files\Shiori\shiori.exe", "index", "rebuild", "--allow",
                @"F:\folder with spaces",
            ],
            startInfo.ArgumentList);
    }
}
