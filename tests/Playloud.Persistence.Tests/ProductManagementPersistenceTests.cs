using Playloud.Application.Catalog;
using Playloud.Domain.Catalog;
using Playloud.Persistence.Sqlite;

namespace Playloud.Persistence.Tests;

public sealed class ProductManagementPersistenceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"playloud-product-management-{Guid.NewGuid():N}.db");

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
    public async Task Product_update_persists_values_and_rejects_duplicate_name()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);
        var adapter = new SqliteCommerceAdapter(store);

        var coffee = Product.Create("Café", 10m, 4m);
        var juice = Product.Create("Suco", 8m, 3m);
        await store.SaveProductAsync(coffee, 5m, cancellationToken);
        await store.SaveProductAsync(juice, 6m, cancellationToken);

        IProductCatalogManager manager = adapter;
        await manager.UpdateAsync(
            Product.Restore(coffee.Id, "Café Premium", 14m, 6m),
            cancellationToken);

        var updated = await store.GetProductAsync(coffee.Id, cancellationToken);
        Assert.NotNull(updated);
        Assert.Equal("Café Premium", updated.Name);
        Assert.Equal(14m, updated.UnitPrice);
        Assert.Equal(6m, updated.UnitCost);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.UpdateAsync(
                Product.Restore(coffee.Id, "suco", 15m, 7m),
                cancellationToken));

        updated = await store.GetProductAsync(coffee.Id, cancellationToken);
        Assert.Equal("Café Premium", updated?.Name);
    }

    [Fact]
    public async Task Stock_adjustment_is_atomic_and_records_ledger_evidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);
        var adapter = new SqliteCommerceAdapter(store);

        var product = Product.Create("Produto", 10m, 4m);
        await store.SaveProductAsync(product, 10m, cancellationToken);

        IProductStockAdjuster adjuster = adapter;
        var current = await adjuster.AdjustAsync(
            product.Id,
            -3m,
            "Contagem de inventário",
            cancellationToken);

        Assert.Equal(7m, current);
        Assert.Equal(7m, await store.GetStockAsync(product.Id, cancellationToken));

        var evidence = Assert.Single(
            await store.GetStockAdjustmentsAsync(product.Id, cancellationToken));
        Assert.Equal(-3m, evidence.QuantityDelta);
        Assert.Equal(7m, evidence.ResultingStock);
        Assert.Equal("Contagem de inventário", evidence.Reason);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adjuster.AdjustAsync(product.Id, -8m, "Baixa inválida", cancellationToken));

        Assert.Equal(7m, await store.GetStockAsync(product.Id, cancellationToken));
        Assert.Single(await store.GetStockAdjustmentsAsync(product.Id, cancellationToken));
    }
}
