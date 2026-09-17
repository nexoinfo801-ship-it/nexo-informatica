using Playloud.Application.Catalog;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Application.Tests;

public sealed class ManageProductTests
{
    [Fact]
    public async Task Product_can_be_renamed_and_repriced_through_catalog_port()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var productId = EntityId<Product>.New();
        var manager = new FakeProductCatalogManager();
        var update = new UpdateProduct(manager);

        var result = await update.ExecuteAsync(
            new UpdateProductCommand(
                productId,
                "  Café Premium  ",
                22.50m,
                11m),
            cancellationToken);

        Assert.Equal(productId, result.ProductId);
        Assert.Equal("Café Premium", result.Name);
        Assert.Equal(22.50m, result.UnitPrice);
        Assert.Equal("Café Premium", manager.UpdatedProduct?.Name);
        Assert.Equal(11m, manager.UpdatedProduct?.UnitCost);
    }

    [Fact]
    public async Task Stock_adjustment_requires_nonzero_delta_and_reason()
    {
        var adjust = new AdjustProductStock(new FakeProductStockAdjuster());
        var productId = EntityId<Product>.New();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            adjust.ExecuteAsync(
                new AdjustProductStockCommand(productId, 0m, "Inventário"),
                TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            adjust.ExecuteAsync(
                new AdjustProductStockCommand(productId, 2m, "   "),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Stock_adjustment_returns_the_persisted_balance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var productId = EntityId<Product>.New();
        var adjuster = new FakeProductStockAdjuster(15m);
        var adjust = new AdjustProductStock(adjuster);

        var result = await adjust.ExecuteAsync(
            new AdjustProductStockCommand(productId, -2m, "  Contagem de inventário  "),
            cancellationToken);

        Assert.Equal(15m, result.CurrentStock);
        Assert.Equal(-2m, adjuster.LastDelta);
        Assert.Equal("Contagem de inventário", adjuster.LastReason);
    }

    private sealed class FakeProductCatalogManager : IProductCatalogManager
    {
        public Product? UpdatedProduct { get; private set; }

        public Task UpdateAsync(
            Product product,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdatedProduct = product;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProductStockAdjuster(decimal currentStock = 0m)
        : IProductStockAdjuster
    {
        public decimal? LastDelta { get; private set; }

        public string? LastReason { get; private set; }

        public Task<decimal> AdjustAsync(
            EntityId<Product> productId,
            decimal quantityDelta,
            string reason,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastDelta = quantityDelta;
            LastReason = reason;
            return Task.FromResult(currentStock);
        }
    }
}
