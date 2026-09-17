using Playloud.Application.Cash;
using Playloud.Application.Catalog;
using Playloud.Application.Sales;
using Playloud.Cliente.Sales;
using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Payments;
using Playloud.Domain.Sales;

namespace Playloud.Cliente.Tests;

public sealed class CommercialCheckoutFlowTests
{
    private static readonly DateOnly BusinessDate = new(2026, 9, 17);

    [Fact]
    public async Task Sale_without_open_cash_is_blocked_and_cart_is_preserved()
    {
        var finalizer = new RecordingFinalizarVenda();
        var viewModel = CreateViewModel(finalizer);
        viewModel.AddProduct(CreateProduct());
        viewModel.AmountReceived = 20m;

        var completed = await viewModel.FinalizeCurrentSaleAsync(
            TestContext.Current.CancellationToken);

        Assert.False(completed);
        Assert.Null(finalizer.LastCommand);
        Assert.Single(viewModel.CartLines);
        Assert.Equal("Abra o caixa antes de finalizar a venda.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Cash_sale_builds_real_command_and_clears_cart_only_after_success()
    {
        var sessionId = EntityId<CashSession>.New();
        var finalizer = new RecordingFinalizarVenda();
        var cash = new RecordingOpenCashSession(
            new ActiveCashSession(sessionId, BusinessDate, 100m));
        var viewModel = CreateViewModel(finalizer, cash);
        var product = CreateProduct();
        viewModel.OpeningBalance = 100m;
        viewModel.AddProduct(product);
        viewModel.AddProduct(product);
        viewModel.AmountReceived = 30m;

        Assert.True(await viewModel.OpenCashSessionAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal(5m, viewModel.ChangeDue);

        var completed = await viewModel.FinalizeCurrentSaleAsync(
            TestContext.Current.CancellationToken);

        Assert.True(completed);
        Assert.NotNull(finalizer.LastCommand);
        Assert.Equal(BusinessDate, finalizer.LastCommand.BusinessDate);
        Assert.Equal(sessionId, finalizer.LastCommand.CashSessionId);
        var item = Assert.Single(finalizer.LastCommand.Items);
        Assert.Equal(product.Id, item.ProductId);
        Assert.Equal(2m, item.Quantity);
        var payment = Assert.Single(finalizer.LastCommand.Payments);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(25m, payment.Amount);
        Assert.Equal(5m, viewModel.LastChangeDue);
        Assert.Empty(viewModel.CartLines);
        Assert.Equal(0m, viewModel.CartSubtotal);
    }

    [Fact]
    public async Task Insufficient_cash_and_failed_commit_both_preserve_cart()
    {
        var cash = new RecordingOpenCashSession(
            new ActiveCashSession(
                EntityId<CashSession>.New(),
                BusinessDate,
                0m));
        var finalizer = new RecordingFinalizarVenda(
            new InvalidOperationException("stock changed"));
        var viewModel = CreateViewModel(finalizer, cash);
        viewModel.AddProduct(CreateProduct());
        await viewModel.OpenCashSessionAsync(TestContext.Current.CancellationToken);

        viewModel.AmountReceived = 10m;
        Assert.False(await viewModel.FinalizeCurrentSaleAsync(
            TestContext.Current.CancellationToken));
        Assert.Null(finalizer.LastCommand);
        Assert.Single(viewModel.CartLines);

        viewModel.AmountReceived = 20m;
        Assert.False(await viewModel.FinalizeCurrentSaleAsync(
            TestContext.Current.CancellationToken));
        Assert.NotNull(finalizer.LastCommand);
        Assert.Single(viewModel.CartLines);
    }

    private static SaleCheckoutViewModel CreateViewModel(
        RecordingFinalizarVenda finalizer,
        IOpenCashSession? cash = null) =>
        new(
            finalizer,
            new EmptySearchProducts(),
            new SaleCart(),
            cash ?? new RecordingOpenCashSession(
                new ActiveCashSession(
                    EntityId<CashSession>.New(),
                    BusinessDate,
                    0m)),
            new FixedTimeProvider(
                new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero)));

    private static ProductSearchResult CreateProduct() =>
        new(EntityId<Product>.New(), "Café Especial", 12.50m, 10m, 6m);

    private sealed class RecordingOpenCashSession(ActiveCashSession result)
        : IOpenCashSession
    {
        public Task<ActiveCashSession> ExecuteAsync(
            OpenCashSessionCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingFinalizarVenda(Exception? exception = null)
        : IFinalizarVenda
    {
        public FinalizarVendaCommand? LastCommand { get; private set; }

        public Task<FinalizarVendaResult> ExecuteAsync(
            FinalizarVendaCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (exception is not null)
            {
                LastCommand = command;
                throw exception;
            }

            LastCommand = command;
            return Task.FromResult(new FinalizarVendaResult(
                EntityId<Sale>.New(),
                command.Items.Sum(item => item.Quantity * 12.50m),
                command.Payments));
        }
    }

    private sealed class EmptySearchProducts : ISearchProducts
    {
        public Task<IReadOnlyList<ProductSearchResult>> ExecuteAsync(
            string query,
            int limit = 20,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductSearchResult>>([]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
