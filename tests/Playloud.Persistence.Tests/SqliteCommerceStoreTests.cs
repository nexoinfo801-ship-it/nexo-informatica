using Playloud.Domain.Catalog;
using Playloud.Domain.Sales;
using Playloud.Persistence.Sqlite;

namespace Playloud.Persistence.Tests;

public sealed class SqliteCommerceStoreTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"playloud-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Database_initialization_enables_foreign_keys_and_wal()
    {
        await using var store = new SqliteCommerceStore(_databasePath);

        await store.InitializeAsync();
        var health = await store.GetHealthAsync();

        Assert.True(health.ForeignKeysEnabled);
        Assert.Equal("wal", health.JournalMode.ToLowerInvariant());
    }

    [Fact]
    public async Task Product_roundtrip_preserves_identity_and_values()
    {
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync();

        var product = Product.Create("Café Especial", 18.90m, 9.40m);
        await store.SaveProductAsync(product, openingStock: 12m);

        var loaded = await store.GetProductAsync(product.Id);

        Assert.NotNull(loaded);
        Assert.Equal(product.Id, loaded.Id);
        Assert.Equal(product.Name, loaded.Name);
        Assert.Equal(product.UnitPrice, loaded.UnitPrice);
        Assert.Equal(product.UnitCost, loaded.UnitCost);
        Assert.Equal(12m, await store.GetStockAsync(product.Id));
    }

    [Fact]
    public async Task Sale_commit_is_atomic_and_decrements_current_stock()
    {
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync();

        var product = Product.Create("Marmita", 25m, 14m);
        await store.SaveProductAsync(product, openingStock: 5m);

        var sale = Sale.Start(new DateOnly(2026, 9, 16));
        sale.AddLine(product, quantity: 2m, availableStock: 5m);

        await store.CommitSaleAsync(sale);

        Assert.Equal(3m, await store.GetStockAsync(product.Id));
        Assert.Equal(1, await store.CountSalesAsync());
        Assert.Equal(1, await store.CountSaleLinesAsync(sale.Id));
    }

    [Fact]
    public async Task Stock_race_rejects_sale_and_rolls_back_everything()
    {
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync();

        var product = Product.Create("Refrigerante", 8m, 3.50m);
        await store.SaveProductAsync(product, openingStock: 1m);

        var sale = Sale.Start(new DateOnly(2026, 9, 16));
        sale.AddLine(product, quantity: 2m, availableStock: 10m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitSaleAsync(sale));

        Assert.Equal(1m, await store.GetStockAsync(product.Id));
        Assert.Equal(0, await store.CountSalesAsync());
        Assert.Equal(0, await store.CountSaleLinesAsync(sale.Id));
    }
}
