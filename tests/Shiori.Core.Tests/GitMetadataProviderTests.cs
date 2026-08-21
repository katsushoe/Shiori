using Shiori.Core.Search;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class GitMetadataProviderTests
{
    [Fact]
    public void GetMetadata_excludes_rooted_and_parent_paths()
    {
        using var workspace = new TemporaryDirectory();
        var provider = new GitMetadataProvider();

        var metadata = provider.GetMetadata(workspace.Path, ["../outside.cs", System.IO.Path.GetFullPath("outside.cs")]);

        Assert.Empty(metadata);
    }

    [Fact]
    public void GetMetadata_returns_untracked_metadata_outside_a_git_repository()
    {
        using var workspace = new TemporaryDirectory();
        var file = System.IO.Path.Combine(workspace.Path, "sample.cs");
        File.WriteAllText(file, "class Sample;");
        var provider = new GitMetadataProvider();

        var metadata = provider.GetMetadata(workspace.Path, ["sample.cs"]);

        Assert.False(metadata["sample.cs"].IsTracked);
        Assert.Null(metadata["sample.cs"].LastWriteTimeUtc);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shiori-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
