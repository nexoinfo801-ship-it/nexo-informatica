using Playloud.Application.Catalog;
using Playloud.Application.Sales;
using Playloud.Cliente.Sales;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Cliente.Tests;

public sealed class ProductManagementViewModelTests
{
    [Fact]
    public async Task Selected_product_can_be_edited_and_refreshed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var product = new ProductSearchResult(
            EntityId<Product>.New(),
            "Café",
            10m,
            5m,
            4m);
        var manager = new FakeCatalogManager();
        var viewModel = CreateViewModel(
            new FakeSearchProducts([
                product with { Name = "Café Premium", UnitPrice = 14m, UnitCost = 6m }]),
            manager,
            new FakeStockAdjuster(5m));

        viewModel.SelectProduct(product);
        Assert.Equal("Café", viewModel.ManagedProductName);
        Assert.Equal(4m, viewModel.ManagedProductUnitCost);

        viewModel.ManagedProductName = "Café Premium";
        viewModel.ManagedProductUnitPrice = 14m;
        viewModel.ManagedProductUnitCost = 6m;

        var updated = await viewModel.UpdateManagedProductAsync(cancellationToken);

        Assert.True(updated);
        Assert.Equal("Café Premium", manager.LastProduct?.Name);
        Assert.Equal(6m, manager.LastProduct?.UnitCost);
        Assert.Equal("Produto atualizado com sucesso.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Selected_product_stock_adjustment_requires_reason_and_refreshes_balance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var product = new ProductSearchResult(
            EntityId<Product>.New(),
            "Suco",
            8m,
            5m,
            3m);
        var adjuster = new FakeStockAdjuster(8m);
        var viewModel = CreateViewModel(
            new FakeSearchProducts([product with { AvailableStock = 8m }]),
            new FakeCatalogManager(),
            adjuster);

        viewModel.SelectProduct(product);
        viewModel.ManagedStockDelta = 3m;
        viewModel.ManagedStockReason = "Entrada de mercadoria";

        var adjusted = await viewModel.AdjustManagedStockAsync(cancellationToken);

        Assert.True(adjusted);
        Assert.Equal(3m, adjuster.LastDelta);
        Assert.Equal("Entrada de mercadoria", adjuster.LastReason);
        Assert.Equal(8m, viewModel.ManagedAvailableStock);
        Assert.Equal(0m, viewModel.ManagedStockDelta);
        Assert.Equal(string.Empty, viewModel.ManagedStockReason);
        Assert.Equal("Estoque ajustado com sucesso.", viewModel.StatusMessage);
    }

    private static SaleCheckoutViewModel CreateViewModel(
        ISearchProducts search,
        IProductCatalogManager manager,
        IProductStockAdjuster adjuster) =>
        new(
            new StubFinalizarVenda(),
            search,
            new SaleCart(),
            new StubCreateProduct(),
            new UpdateProduct(manager),
            new AdjustProductStock(adjuster));

    private sealed class FakeCatalogManager : IProductCatalogManager
    {
        public Product? LastProduct { get; private set; }

        public Task UpdateAsync(
            Product product,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastProduct = product;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStockAdjuster(decimal currentStock)
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

    private sealed class FakeSearchProducts(
        IReadOnlyList<ProductSearchResult> results) : ISearchProducts
    {
        public Task<IReadOnlyList<ProductSearchResult>> ExecuteAsync(
            string query,
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(results);
        }
    }

    private sealed class StubCreateProduct : ICreateProduct
    {
        public Task<CreateProductResult> ExecuteAsync(
            CreateProductCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubFinalizarVenda : IFinalizarVenda
    {
        public Task<FinalizarVendaResult> ExecuteAsync(
            FinalizarVendaCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
