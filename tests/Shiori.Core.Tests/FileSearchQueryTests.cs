using Shiori.Core.Engine;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class FileSearchQueryTests
{
    [Fact]
    public void Create_WhenOnlyPrefixIsGiven_KeepsPrefixAndDropsBlankConditions()
    {
        var query = FileSearchQuery.Create("  ", " Thunderbird ", null);

        Assert.Null(query.Text);
        Assert.Equal("Thunderbird", query.NameStartsWith);
        Assert.Null(query.NameEndsWith);
    }

    [Fact]
    public void Create_WhenEveryConditionIsBlank_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => FileSearchQuery.Create(null, " ", ""));
    }

    [Theory]
    [InlineData("text", "prefix", "suffix", "text")]
    [InlineData(null, "prefix", "suffix", "prefix")]
    [InlineData(null, null, "suffix", "suffix")]
    public void RankingTerm_PrefersTextThenPrefixThenSuffix(
        string? text,
        string? prefix,
        string? suffix,
        string expected)
    {
        var query = FileSearchQuery.Create(text, prefix, suffix);

        Assert.Equal(expected, query.RankingTerm);
    }

    [Fact]
    public void ToString_ListsOnlySuppliedConditions()
    {
        var query = FileSearchQuery.Create(null, "First", ".cs");

        Assert.Equal("nameStartsWith=First nameEndsWith=.cs", query.ToString());
    }
}
