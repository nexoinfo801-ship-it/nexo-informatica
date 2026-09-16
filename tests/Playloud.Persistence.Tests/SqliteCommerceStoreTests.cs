using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Payments;
using Playloud.Domain.Sales;
using Playloud.Persistence.Sqlite;

namespace Playloud.Persistence.Tests;

public sealed class SqliteCommerceStoreTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"playloud-{Guid.NewGuid():N}.db");

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
    public async Task Database_initialization_enables_foreign_keys_and_wal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);

        await store.InitializeAsync(cancellationToken);
        var health = await store.GetHealthAsync(cancellationToken);

        Assert.True(health.ForeignKeysEnabled);
        Assert.Equal("wal", health.JournalMode.ToLowerInvariant());
    }

    [Fact]
    public async Task Product_roundtrip_preserves_identity_and_values()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var product = Product.Create("Café Especial", 18.90m, 9.40m);
        await store.SaveProductAsync(product, openingStock: 12m, cancellationToken);

        var loaded = await store.GetProductAsync(product.Id, cancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(product.Id, loaded.Id);
        Assert.Equal(product.Name, loaded.Name);
        Assert.Equal(product.UnitPrice, loaded.UnitPrice);
        Assert.Equal(product.UnitCost, loaded.UnitCost);
        Assert.Equal(12m, await store.GetStockAsync(product.Id, cancellationToken));
    }

    [Fact]
    public async Task Cash_sale_commit_is_atomic_across_stock_payment_and_cash_movement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var product = Product.Create("Marmita", 25m, 14m);
        await store.SaveProductAsync(product, openingStock: 5m, cancellationToken);

        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 100m);
        await store.SaveCashSessionAsync(session, cancellationToken);

        var sale = Sale.Start(new DateOnly(2026, 9, 16));
        sale.AddLine(product, quantity: 2m, availableStock: 5m);
        sale.Complete([Payment.Create(PaymentMethod.Cash, 50m)]);

        await store.CommitSaleAsync(sale, session.Id, cancellationToken);

        Assert.Equal(3m, await store.GetStockAsync(product.Id, cancellationToken));
        Assert.Equal(1, await store.CountSalesAsync(cancellationToken));
        Assert.Equal(1, await store.CountSaleLinesAsync(sale.Id, cancellationToken));
        Assert.Equal(1, await store.CountPaymentsAsync(sale.Id, cancellationToken));
        Assert.Equal(1, await store.CountCashMovementsAsync(session.Id, cancellationToken));
        Assert.Equal(150m, await store.GetExpectedCashBalanceAsync(session.Id, cancellationToken));
    }

    [Fact]
    public async Task Pix_sale_persists_payment_without_changing_physical_cash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var product = Product.Create("Suco", 10m, 4m);
        await store.SaveProductAsync(product, openingStock: 2m, cancellationToken);

        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 20m);
        await store.SaveCashSessionAsync(session, cancellationToken);

        var sale = Sale.Start(new DateOnly(2026, 9, 16));
        sale.AddLine(product, quantity: 1m, availableStock: 2m);
        sale.Complete([Payment.Create(PaymentMethod.Pix, 10m)]);

        await store.CommitSaleAsync(sale, session.Id, cancellationToken);

        Assert.Equal(1, await store.CountPaymentsAsync(sale.Id, cancellationToken));
        Assert.Equal(0, await store.CountCashMovementsAsync(session.Id, cancellationToken));
        Assert.Equal(20m, await store.GetExpectedCashBalanceAsync(session.Id, cancellationToken));
    }

    [Fact]
    public async Task Stock_race_rolls_back_sale_payment_cash_movement_and_stock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var product = Product.Create("Refrigerante", 8m, 3.50m);
        await store.SaveProductAsync(product, openingStock: 1m, cancellationToken);

        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 0m);
        await store.SaveCashSessionAsync(session, cancellationToken);

        var sale = Sale.Start(new DateOnly(2026, 9, 16));
        sale.AddLine(product, quantity: 2m, availableStock: 10m);
        sale.Complete([Payment.Create(PaymentMethod.Cash, 16m)]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CommitSaleAsync(sale, session.Id, cancellationToken));

        Assert.Equal(1m, await store.GetStockAsync(product.Id, cancellationToken));
        Assert.Equal(0, await store.CountSalesAsync(cancellationToken));
        Assert.Equal(0, await store.CountSaleLinesAsync(sale.Id, cancellationToken));
        Assert.Equal(0, await store.CountPaymentsAsync(sale.Id, cancellationToken));
        Assert.Equal(0, await store.CountCashMovementsAsync(session.Id, cancellationToken));
        Assert.Equal(0m, await store.GetExpectedCashBalanceAsync(session.Id, cancellationToken));
    }

    [Fact]
    public async Task Incomplete_sale_is_rejected_before_any_database_change()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var product = Product.Create("Pão", 2m, 0.80m);
        await store.SaveProductAsync(product, openingStock: 10m, cancellationToken);

        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 0m);
        await store.SaveCashSessionAsync(session, cancellationToken);

        var sale = Sale.Start(new DateOnly(2026, 9, 16));
        sale.AddLine(product, quantity: 1m, availableStock: 10m);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CommitSaleAsync(sale, session.Id, cancellationToken));

        Assert.Equal(10m, await store.GetStockAsync(product.Id, cancellationToken));
        Assert.Equal(0, await store.CountSalesAsync(cancellationToken));
        Assert.Equal(0, await store.CountPaymentsAsync(sale.Id, cancellationToken));
    }
}
