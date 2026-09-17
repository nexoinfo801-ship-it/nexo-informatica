using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Application.Catalog;

public sealed record UpdateProductCommand(
    EntityId<Product> ProductId,
    string Name,
    decimal UnitPrice,
    decimal UnitCost);

public sealed record UpdateProductResult(
    EntityId<Product> ProductId,
    string Name,
    decimal UnitPrice,
    decimal UnitCost);

public interface IProductCatalogManager
{
    Task UpdateAsync(
        Product product,
        CancellationToken cancellationToken = default);
}

public sealed class UpdateProduct(IProductCatalogManager manager)
{
    private readonly IProductCatalogManager _manager =
        manager ?? throw new ArgumentNullException(nameof(manager));

    public async Task<UpdateProductResult> ExecuteAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = Product.Restore(
            command.ProductId,
            command.Name,
            command.UnitPrice,
            command.UnitCost);

        await _manager.UpdateAsync(product, cancellationToken);

        return new UpdateProductResult(
            product.Id,
            product.Name,
            product.UnitPrice,
            product.UnitCost);
    }
}

public sealed record AdjustProductStockCommand(
    EntityId<Product> ProductId,
    decimal QuantityDelta,
    string Reason);

public sealed record AdjustProductStockResult(
    EntityId<Product> ProductId,
    decimal QuantityDelta,
    decimal CurrentStock);

public interface IProductStockAdjuster
{
    Task<decimal> AdjustAsync(
        EntityId<Product> productId,
        decimal quantityDelta,
        string reason,
        CancellationToken cancellationToken = default);
}

public sealed class AdjustProductStock(IProductStockAdjuster adjuster)
{
    private readonly IProductStockAdjuster _adjuster =
        adjuster ?? throw new ArgumentNullException(nameof(adjuster));

    public async Task<AdjustProductStockResult> ExecuteAsync(
        AdjustProductStockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.QuantityDelta == 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "Stock adjustment must change the current quantity.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new ArgumentException(
                "Stock adjustment reason is required.",
                nameof(command));
        }

        var currentStock = await _adjuster.AdjustAsync(
            command.ProductId,
            command.QuantityDelta,
            command.Reason.Trim(),
            cancellationToken);

        return new AdjustProductStockResult(
            command.ProductId,
            command.QuantityDelta,
            currentStock);
    }
}
