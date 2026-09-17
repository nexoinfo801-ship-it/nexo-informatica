using Playloud.Application.Catalog;
using Playloud.Domain.Catalog;

namespace Playloud.Application.Tests;

public sealed class CreateProductTests
{
    [Fact]
    public async Task Valid_product_is_created_with_stock_and_stable_identity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var writer = new FakeProductCatalogWriter();
        var createProduct = new CreateProduct(writer);

        var result = await createProduct.ExecuteAsync(
            new CreateProductCommand("  Café Especial  ", 18.90m, 9.40m, 12m),
            cancellationToken);

        Assert.NotEqual(Guid.Empty, result.ProductId.Value);
        Assert.Equal("Café Especial", result.Name);
        Assert.Equal(18.90m, result.UnitPrice);
        Assert.Equal(12m, result.OpeningStock);
        Assert.NotNull(writer.SavedProduct);
        Assert.Equal(result.ProductId, writer.SavedProduct.Id);
        Assert.Equal(12m, writer.OpeningStock);
    }

    [Fact]
    public async Task Negative_opening_stock_is_rejected_before_persistence()
    {
        var writer = new FakeProductCatalogWriter();
        var createProduct = new CreateProduct(writer);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            createProduct.ExecuteAsync(
                new CreateProductCommand("Produto", 10m, 4m, -1m),
                TestContext.Current.CancellationToken));

        Assert.Null(writer.SavedProduct);
    }

    [Fact]
    public async Task Invalid_commercial_values_use_domain_validation()
    {
        var createProduct = new CreateProduct(new FakeProductCatalogWriter());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            createProduct.ExecuteAsync(
                new CreateProductCommand("   ", 10m, 4m, 1m),
                TestContext.Current.CancellationToken));
    }

    private sealed class FakeProductCatalogWriter : IProductCatalogWriter
    {
        public Product? SavedProduct { get; private set; }

        public decimal? OpeningStock { get; private set; }

        public Task SaveAsync(
            Product product,
            decimal openingStock,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedProduct = product;
            OpeningStock = openingStock;
            return Task.CompletedTask;
        }
    }
}
