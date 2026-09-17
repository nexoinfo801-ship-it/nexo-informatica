using Playloud.Application.Catalog;
using Playloud.Application.Sales;
using Playloud.Cliente.Sales;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Cliente.Tests;

public sealed class ProductRegistrationViewModelTests
{
    [Fact]
    public async Task Valid_registration_clears_form_and_refreshes_search()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var productId = EntityId<Product>.New();
        var create = new FakeCreateProduct(new CreateProductResult(
            productId,
            "Café Especial",
            18.90m,
            12m));
        var search = new FakeSearchProducts([
            new ProductSearchResult(productId, "Café Especial", 18.90m, 12m)]);
        var viewModel = new SaleCheckoutViewModel(
            new StubFinalizarVenda(),
            search,
            new SaleCart(),
            create)
        {
            NewProductName = "Café Especial",
            NewProductUnitPrice = 18.90m,
            NewProductUnitCost = 9.40m,
            NewProductOpeningStock = 12m
        };

        var created = await viewModel.CreateProductAsync(cancellationToken);

        Assert.True(created);
        Assert.Equal("Café Especial", create.LastCommand?.Name);
        Assert.Equal(18.90m, create.LastCommand?.UnitPrice);
        Assert.Equal(string.Empty, viewModel.NewProductName);
        Assert.Equal(0m, viewModel.NewProductUnitPrice);
        Assert.Equal("Café Especial", viewModel.SearchText);
        Assert.Single(viewModel.SearchResults);
        Assert.Equal("Produto cadastrado e pronto para venda.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Duplicate_name_is_reported_without_clearing_form()
    {
        var create = new FakeCreateProduct(
            new InvalidOperationException("duplicate"));
        var viewModel = new SaleCheckoutViewModel(
            new StubFinalizarVenda(),
            new FakeSearchProducts([]),
            new SaleCart(),
            create)
        {
            NewProductName = "Café"
        };

        var created = await viewModel.CreateProductAsync(
            TestContext.Current.CancellationToken);

        Assert.False(created);
        Assert.Equal("Café", viewModel.NewProductName);
        Assert.Equal(
            "Já existe um produto com este nome.",
            viewModel.StatusMessage);
    }

    private sealed class FakeCreateProduct : ICreateProduct
    {
        private readonly CreateProductResult? _result;
        private readonly Exception? _exception;

        public FakeCreateProduct(CreateProductResult result) => _result = result;

        public FakeCreateProduct(Exception exception) => _exception = exception;

        public CreateProductCommand? LastCommand { get; private set; }

        public Task<CreateProductResult> ExecuteAsync(
            CreateProductCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCommand = command;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result!);
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

    private sealed class StubFinalizarVenda : IFinalizarVenda
    {
        public Task<FinalizarVendaResult> ExecuteAsync(
            FinalizarVendaCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
