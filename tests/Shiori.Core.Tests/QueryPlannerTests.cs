using Shiori.Core.Search;
using Xunit;

namespace Shiori.Core.Tests;

public sealed class QueryPlannerTests
{
    [Theory]
    [InlineData("AccountDTO.cs", SearchIntent.File, "AccountDTO.cs", SearchProvider.File)]
    [InlineData("SaveAccount", SearchIntent.Symbol, "SaveAccount", SearchProvider.Symbol)]
    [InlineData("\"Application-specific password required\"", SearchIntent.Text,
        "Application-specific password required", SearchProvider.Text)]
    [InlineData("UPDATE accounts SET active = 1", SearchIntent.Text,
        "UPDATE accounts SET active = 1", SearchProvider.Text)]
    public void Plan_selects_primary_provider(
        string query,
        SearchIntent expectedIntent,
        string expectedQuery,
        SearchProvider expectedProvider)
    {
        var plan = QueryPlanner.Plan(query);

        Assert.Equal(expectedIntent, plan.Intent);
        Assert.Equal(expectedQuery, plan.SearchQuery);
        Assert.Equal(expectedProvider, plan.Providers[0]);
    }

    [Theory]
    [InlineData("AccountDTOはどこ？", SearchIntent.Symbol, "AccountDTO")]
    [InlineData("SaveAccountを呼び出している場所", SearchIntent.References, "SaveAccount")]
    [InlineData("AccountDTOを継承しているクラス", SearchIntent.Implementations, "AccountDTO")]
    [InlineData("find references to SaveAccount", SearchIntent.References, "SaveAccount")]
    public void Plan_extracts_identifier_from_navigation_intent(
        string query,
        SearchIntent expectedIntent,
        string expectedQuery)
    {
        var plan = QueryPlanner.Plan(query);

        Assert.Equal(expectedIntent, plan.Intent);
        Assert.Equal(expectedQuery, plan.SearchQuery);
        Assert.Contains(SearchProvider.Symbol, plan.Providers);
    }

    [Fact]
    public void Plan_rejects_empty_query()
    {
        Assert.Throws<ArgumentException>(() => QueryPlanner.Plan("  "));
    }
}
