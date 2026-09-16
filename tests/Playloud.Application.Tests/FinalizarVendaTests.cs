using Playloud.Application.Sales;
using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Payments;
using Playloud.Domain.Sales;

namespace Playloud.Application.Tests;

public sealed class FinalizarVendaTests
{
    [Fact]
    public async Task Successful_cash_sale_commits_once_and_returns_summary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var product = Product.Create("Café", 12.50m, 6m);
        var reader = new FakeProductSnapshotReader(new ProductSnapshot(
            product.Id,
            product.Name,
            product.UnitPrice,
            product.UnitCost,
            AvailableStock: 10m));
        var committer = new FakeSaleCommitter();
        var useCase = new FinalizarVenda(reader, committer);
        var cashSessionId = EntityId<CashSession>.New();

        var result = await useCase.ExecuteAsync(
            new FinalizarVendaCommand(
                new DateOnly(2026, 9, 16),
                cashSessionId,
                [new FinalizarVendaItem(product.Id, 2m)],
                [new FinalizarVendaPayment(PaymentMethod.Cash, 25m)]),
            cancellationToken);

        Assert.Equal(1, committer.CommitCount);
        Assert.NotNull(committer.LastSale);
        Assert.True(committer.LastSale.IsCompleted);
        Assert.Equal(cashSessionId, committer.LastCashSessionId);
        Assert.Equal(25m, result.GrossTotal);
        Assert.Equal(committer.LastSale.Id, result.SaleId);
        Assert.Single(result.Payments);
        Assert.Equal(PaymentMethod.Cash, result.Payments[0].Method);
        Assert.Equal(25m, result.Payments[0].Amount);
    }

    [Fact]
    public async Task Mixed_payment_is_preserved_exactly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var product = Product.Create("Kit", 50m, 20m);
        var reader = new FakeProductSnapshotReader(new ProductSnapshot(
            product.Id,
            product.Name,
            product.UnitPrice,
            product.UnitCost,
            AvailableStock: 5m));
        var committer = new FakeSaleCommitter();
        var useCase = new FinalizarVenda(reader, committer);

        var result = await useCase.ExecuteAsync(
            new FinalizarVendaCommand(
                new DateOnly(2026, 9, 16),
                EntityId<CashSession>.New(),
                [new FinalizarVendaItem(product.Id, 2m)],
                [
                    new FinalizarVendaPayment(PaymentMethod.Cash, 40m),
                    new FinalizarVendaPayment(PaymentMethod.Pix, 60m)
                ]),
            cancellationToken);

        Assert.Equal(100m, result.GrossTotal);
        Assert.Collection(
            result.Payments,
            payment =>
            {
                Assert.Equal(PaymentMethod.Cash, payment.Method);
                Assert.Equal(40m, payment.Amount);
            },
            payment =>
            {
                Assert.Equal(PaymentMethod.Pix, payment.Method);
                Assert.Equal(60m, payment.Amount);
            });
    }

    [Fact]
    public async Task Missing_product_fails_before_persistence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var missingProductId = EntityId<Product>.New();
        var reader = new FakeProductSnapshotReader();
        var committer = new FakeSaleCommitter();
        var useCase = new FinalizarVenda(reader, committer);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            useCase.ExecuteAsync(
                new FinalizarVendaCommand(
                    new DateOnly(2026, 9, 16),
                    EntityId<CashSession>.New(),
                    [new FinalizarVendaItem(missingProductId, 1m)],
                    [new FinalizarVendaPayment(PaymentMethod.Cash, 1m)]),
                cancellationToken));

        Assert.Equal(0, committer.CommitCount);
    }

    [Fact]
    public async Task Insufficient_stock_fails_before_persistence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var product = Product.Create("Produto", 10m, 4m);
        var reader = new FakeProductSnapshotReader(new ProductSnapshot(
            product.Id,
            product.Name,
            product.UnitPrice,
            product.UnitCost,
            AvailableStock: 1m));
        var committer = new FakeSaleCommitter();
        var useCase = new FinalizarVenda(reader, committer);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ExecuteAsync(
                new FinalizarVendaCommand(
                    new DateOnly(2026, 9, 16),
                    EntityId<CashSession>.New(),
                    [new FinalizarVendaItem(product.Id, 2m)],
                    [new FinalizarVendaPayment(PaymentMethod.Cash, 20m)]),
                cancellationToken));

        Assert.Equal(0, committer.CommitCount);
    }

    private sealed class FakeProductSnapshotReader(params ProductSnapshot[] products) : IProductSnapshotReader
    {
        private readonly Dictionary<EntityId<Product>, ProductSnapshot> _products =
            products.ToDictionary(static product => product.Id);

        public Task<ProductSnapshot?> ReadAsync(
            EntityId<Product> productId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_products.GetValueOrDefault(productId));
        }
    }

    private sealed class FakeSaleCommitter : ISaleCommitter
    {
        public int CommitCount { get; private set; }

        public Sale? LastSale { get; private set; }

        public EntityId<CashSession>? LastCashSessionId { get; private set; }

        public Task CommitSaleAsync(
            Sale sale,
            EntityId<CashSession> cashSessionId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitCount++;
            LastSale = sale;
            LastCashSessionId = cashSessionId;
            return Task.CompletedTask;
        }
    }
}
