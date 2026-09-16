using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Domain.Inventory;

public sealed class StockItem
{
    public StockItem(EntityId<Product> productId, decimal quantity)
    {
        if (quantity < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Stock cannot start negative.");
        }

        ProductId = productId;
        Quantity = quantity;
    }

    public EntityId<Product> ProductId { get; }

    public decimal Quantity { get; private set; }

    public void Add(decimal quantity)
    {
        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        Quantity += quantity;
    }

    public void Remove(decimal quantity)
    {
        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        if (quantity > Quantity)
        {
            throw new InvalidOperationException("Insufficient stock.");
        }

        Quantity -= quantity;
    }
}
