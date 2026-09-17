using Playloud.Application.Catalog;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Application.Tests;

public sealed class SearchProductsTests
{
    [Fact]
    public async Task Search_trims_the_commercial_term_and_returns_available_products()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new ProductSearchResult(
            EntityId<Product>.New(),
            "Café Especial",
            18.90m,
            12m);
        var reader = new FakeProductSearchReader([expected]);
        var search = new SearchProducts(reader);

        var result = await search.ExecuteAsync("  café  ", 20, cancellationToken);

        Assert.Equal([expected], result);
        Assert.Equal("café", reader.LastQuery);
        Assert.Equal(20, reader.LastLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Search_rejects_an_unsafe_result_limit(int limit)
    {
        var search = new SearchProducts(new FakeProductSearchReader([]));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            search.ExecuteAsync("café", limit, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Blank_search_does_not_query_the_catalog()
    {
        var reader = new FakeProductSearchReader([]);
        var search = new SearchProducts(reader);

        var result = await search.ExecuteAsync("   ", 20, TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Null(reader.LastQuery);
    }

    private sealed class FakeProductSearchReader(
        IReadOnlyList<ProductSearchResult> result) : IProductSearchReader
    {
        public string? LastQuery { get; private set; }

        public int? LastLimit { get; private set; }

        public Task<IReadOnlyList<ProductSearchResult>> SearchAsync(
            string query,
            int limit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastQuery = query;
            LastLimit = limit;
            return Task.FromResult(result);
        }
    }
}
