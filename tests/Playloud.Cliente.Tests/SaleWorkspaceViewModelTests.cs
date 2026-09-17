using Playloud.Application.Catalog;
using Playloud.Application.Sales;
using Playloud.Cliente.Sales;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Cliente.Tests;

public sealed class SaleWorkspaceViewModelTests
{
    [Fact]
    public async Task Search_exposes_commercial_results_without_technical_ids()
    {
        var product = CreateProduct(availableStock: 5m);
        var search = new FakeSearchProducts([product]);
        var viewModel = CreateViewModel(search);
        viewModel.SearchText = "  café  ";

        await viewModel.SearchAsync(TestContext.Current.CancellationToken);

        Assert.Equal("  café  ", search.LastQuery);
        Assert.Equal([product], viewModel.SearchResults);
        Assert.Equal("1 produto encontrado.", viewModel.StatusMessage);
    }

    [Fact]
    public void Adding_result_updates_cart_quantity_and_subtotal()
    {
        var product = CreateProduct(availableStock: 5m);
        var viewModel = CreateViewModel(new FakeSearchProducts([]));

        viewModel.AddProduct(product);
        viewModel.AddProduct(product);

        var line = Assert.Single(viewModel.CartLines);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(25m, viewModel.CartSubtotal);
        Assert.True(viewModel.HasCartItems);
    }

    [Fact]
    public void Quantity_and_removal_actions_keep_cart_consistent()
    {
        var product = CreateProduct(availableStock: 5m);
        var viewModel = CreateViewModel(new FakeSearchProducts([]));
        viewModel.AddProduct(product);

        viewModel.IncreaseQuantity(product.Id);
        Assert.Equal(2m, Assert.Single(viewModel.CartLines).Quantity);

        viewModel.DecreaseQuantity(product.Id);
        Assert.Equal(1m, Assert.Single(viewModel.CartLines).Quantity);

        viewModel.RemoveProduct(product.Id);
        Assert.Empty(viewModel.CartLines);
        Assert.Equal(0m, viewModel.CartSubtotal);
        Assert.False(viewModel.HasCartItems);
    }

    [Fact]
    public void Stock_limit_is_reported_without_corrupting_cart()
    {
        var product = CreateProduct(availableStock: 1m);
        var viewModel = CreateViewModel(new FakeSearchProducts([]));
        viewModel.AddProduct(product);

        viewModel.AddProduct(product);

        Assert.Equal(1m, Assert.Single(viewModel.CartLines).Quantity);
        Assert.Equal("Estoque insuficiente para aumentar este item.", viewModel.StatusMessage);
    }

    private static SaleCheckoutViewModel CreateViewModel(ISearchProducts search) =>
        new(new StubFinalizarVenda(), search, new SaleCart());

    private static ProductSearchResult CreateProduct(decimal availableStock) =>
        new(EntityId<Product>.New(), "Café Especial", 12.50m, availableStock);

    private sealed class FakeSearchProducts(
        IReadOnlyList<ProductSearchResult> results) : ISearchProducts
    {
        public string? LastQuery { get; private set; }

        public Task<IReadOnlyList<ProductSearchResult>> ExecuteAsync(
            string query,
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastQuery = query;
            return Task.FromResult(results);
        }
    }

    private sealed class StubFinalizarVenda : IFinalizarVenda
    {
        public Task<FinalizarVendaResult> ExecuteAsync(
            FinalizarVendaCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
