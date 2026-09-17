using Playloud.Application.Catalog;
using Playloud.Application.Sales;
using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Sales;

namespace Playloud.Persistence.Sqlite;

public sealed class SqliteCommerceAdapter(
    SqliteCommerceStore store) : IProductSnapshotReader, IProductSearchReader, IProductCatalogWriter, IProductCatalogManager, IProductStockAdjuster, ISaleCommitter
{
    private readonly SqliteCommerceStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<ProductSnapshot?> ReadAsync(
        EntityId<Product> productId,
        CancellationToken cancellationToken = default)
    {
        var product = await _store.GetProductAsync(productId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        var availableStock = await _store.GetStockAsync(productId, cancellationToken);

        return new ProductSnapshot(
            product.Id,
            product.Name,
            product.UnitPrice,
            product.UnitCost,
            availableStock);
    }

    public Task CommitSaleAsync(
        Sale sale,
        EntityId<CashSession> cashSessionId,
        CancellationToken cancellationToken = default) =>
        _store.CommitSaleAsync(sale, cashSessionId, cancellationToken);

    public Task SaveAsync(
        Product product,
        decimal openingStock,
        CancellationToken cancellationToken = default) =>
        _store.SaveProductAsync(product, openingStock, cancellationToken);

    public Task UpdateAsync(
        Product product,
        CancellationToken cancellationToken = default) =>
        _store.UpdateProductAsync(product, cancellationToken);

    public Task<decimal> AdjustAsync(
        EntityId<Product> productId,
        decimal quantityDelta,
        string reason,
        CancellationToken cancellationToken = default) =>
        _store.AdjustStockAsync(productId, quantityDelta, reason, cancellationToken);

    public async Task<IReadOnlyList<ProductSearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var products = await _store.SearchProductsAsync(query, limit, cancellationToken);
        return products
            .Select(static product => new ProductSearchResult(
                product.Id,
                product.Name,
                product.UnitPrice,
                product.AvailableStock))
            .ToArray();
    }
}
