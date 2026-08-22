using Shiori.Core.Engine;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class IndexProgressTests
{
    [Theory]
    [InlineData(0UL, 3UL, 0)]
    [InlineData(1UL, 3UL, 33)]
    [InlineData(2UL, 3UL, 66)]
    [InlineData(3UL, 3UL, 100)]
    public void Percent_WithDirectoryProgress_ReturnsFloorPercentage(
        ulong completed,
        ulong total,
        int expected)
    {
        var progress = new IndexProgress(completed, total, ".");

        Assert.Equal(expected, progress.Percent);
    }
}
