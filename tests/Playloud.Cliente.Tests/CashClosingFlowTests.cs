using Playloud.Application.Cash;
using Playloud.Application.Catalog;
using Playloud.Application.Sales;
using Playloud.Cliente.Sales;
using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Sales;

namespace Playloud.Cliente.Tests;

public sealed class CashClosingFlowTests
{
    private static readonly DateOnly BusinessDate = new(2026, 10, 1);

    [Fact]
    public async Task Closing_open_cash_updates_state_and_exposes_ledger_difference()
    {
        var sessionId = EntityId<CashSession>.New();
        var opener = new RecordingOpenCashSession(
            new ActiveCashSession(sessionId, BusinessDate, 100m));
        var closer = new RecordingCloseCashSession(
            new CashClosing(ExpectedCash: 125m, ActualCash: 123m, Difference: -2m));

        var viewModel = CreateViewModel(opener, closer);
        viewModel.OpeningBalance = 100m;

        Assert.True(await viewModel.OpenCashSessionAsync(
            TestContext.Current.CancellationToken));

        viewModel.ActualCash = 123m;

        var closed = await viewModel.CloseCashSessionAsync(
            TestContext.Current.CancellationToken);

        Assert.True(closed);
        Assert.False(viewModel.HasOpenCashSession);
        Assert.NotNull(viewModel.LastCashClosing);
        Assert.Equal(125m, viewModel.LastCashClosing!.ExpectedCash);
        Assert.Equal(123m, viewModel.LastCashClosing.ActualCash);
        Assert.Equal(-2m, viewModel.LastCashClosing.Difference);
        Assert.Equal(sessionId, closer.LastSessionId);
        Assert.Equal(123m, closer.LastActualCash);
        Assert.Contains("Caixa fechado", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Negative_actual_cash_is_blocked_and_session_remains_open()
    {
        var sessionId = EntityId<CashSession>.New();
        var opener = new RecordingOpenCashSession(
            new ActiveCashSession(sessionId, BusinessDate, 100m));
        var closer = new RecordingCloseCashSession(
            new CashClosing(ExpectedCash: 100m, ActualCash: 100m, Difference: 0m));

        var viewModel = CreateViewModel(opener, closer);
        Assert.True(await viewModel.OpenCashSessionAsync(
            TestContext.Current.CancellationToken));

        viewModel.ActualCash = -1m;

        var closed = await viewModel.CloseCashSessionAsync(
            TestContext.Current.CancellationToken);

        Assert.False(closed);
        Assert.True(viewModel.HasOpenCashSession);
        Assert.Null(closer.LastSessionId);
        Assert.Contains("não pode ser negativo", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static SaleCheckoutViewModel CreateViewModel(
        IOpenCashSession opener,
        ICloseCashSession closer) =>
        new(
            new EmptyFinalizer(),
            new EmptySearchProducts(),
            new SaleCart(),
            new EmptyCreateProduct(),
            new UpdateProduct(new EmptyProductCatalogManager()),
            new AdjustProductStock(new EmptyProductStockAdjuster()),
            opener,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero)),
            closer);

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

    private sealed class RecordingCloseCashSession(CashClosing result)
        : ICloseCashSession
    {
        public EntityId<CashSession>? LastSessionId { get; private set; }

        public decimal? LastActualCash { get; private set; }

        public Task<CashClosing> ExecuteAsync(
            CloseCashSessionCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSessionId = command.CashSessionId;
            LastActualCash = command.ActualCash;
            return Task.FromResult(result);
        }
    }

    private sealed class EmptyFinalizer : IFinalizarVenda
    {
        public Task<FinalizarVendaResult> ExecuteAsync(
            FinalizarVendaCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptySearchProducts : ISearchProducts
    {
        public Task<IReadOnlyList<ProductSearchResult>> ExecuteAsync(
            string query,
            int limit = 20,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductSearchResult>>([]);
    }

    private sealed class EmptyCreateProduct : ICreateProduct
    {
        public Task<CreateProductResult> ExecuteAsync(
            CreateProductCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyProductCatalogManager : IProductCatalogManager
    {
        public Task UpdateAsync(
            Product product,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyProductStockAdjuster : IProductStockAdjuster
    {
        public Task<decimal> AdjustAsync(
            EntityId<Product> productId,
            decimal quantityDelta,
            string reason,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
