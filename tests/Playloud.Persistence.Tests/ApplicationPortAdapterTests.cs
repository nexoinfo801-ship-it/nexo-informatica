using Playloud.Application.Sales;
using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Payments;
using Playloud.Persistence.Sqlite;

namespace Playloud.Persistence.Tests;

public sealed class ApplicationPortAdapterTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"playloud-application-adapter-{Guid.NewGuid():N}.db");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task FinalizarVenda_uses_sqlite_ports_and_preserves_atomic_commerce_rules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var product = Product.Create("Café", 12.50m, 6m);
        await store.SaveProductAsync(product, openingStock: 10m, cancellationToken);

        var cashSession = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 100m);
        await store.SaveCashSessionAsync(cashSession, cancellationToken);

        IProductSnapshotReader reader = store;
        ISaleCommitter committer = store;
        var useCase = new FinalizarVenda(reader, committer);

        var result = await useCase.ExecuteAsync(
            new FinalizarVendaCommand(
                new DateOnly(2026, 9, 16),
                cashSession.Id,
                [new FinalizarVendaItem(product.Id, 2m)],
                [new FinalizarVendaPayment(PaymentMethod.Cash, 25m)]),
            cancellationToken);

        Assert.Equal(25m, result.GrossTotal);
        Assert.Equal(8m, await store.GetStockAsync(product.Id, cancellationToken));
        Assert.Equal(1, await store.CountSalesAsync(cancellationToken));
        Assert.Equal(125m, await store.GetExpectedCashBalanceAsync(cashSession.Id, cancellationToken));
    }

    [Fact]
    public async Task Product_snapshot_reader_returns_stable_identity_and_current_stock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var product = Product.Create("Produto", 30m, 12m);
        await store.SaveProductAsync(product, openingStock: 7m, cancellationToken);

        IProductSnapshotReader reader = store;
        var snapshot = await reader.ReadAsync(product.Id, cancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(product.Id, snapshot.Id);
        Assert.Equal(product.Name, snapshot.Name);
        Assert.Equal(product.UnitPrice, snapshot.UnitPrice);
        Assert.Equal(product.UnitCost, snapshot.UnitCost);
        Assert.Equal(7m, snapshot.AvailableStock);
    }
}
