using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Domain.Sales;

public sealed class Sale
{
    private readonly List<SaleLine> _lines = [];

    private Sale(DateOnly businessDate)
    {
        Id = EntityId<Sale>.New();
        BusinessDate = businessDate;
    }

    public EntityId<Sale> Id { get; }

    public DateOnly BusinessDate { get; }

    public IReadOnlyList<SaleLine> Lines => _lines;

    public static Sale Start(DateOnly businessDate) => new(businessDate);

    public void AddLine(Product product, decimal quantity, decimal availableStock)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        if (availableStock < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(availableStock), "Available stock cannot be negative.");
        }

        if (quantity > availableStock)
        {
            throw new InvalidOperationException("Requested quantity exceeds available stock.");
        }

        _lines.Add(new SaleLine(
            EntityId<SaleLine>.New(),
            product.Id,
            product.Name,
            quantity,
            product.UnitPrice,
            product.UnitCost));
    }
}

public sealed record SaleLine(
    EntityId<SaleLine> Id,
    EntityId<Product> ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal UnitCost)
{
    public decimal GrossTotal => Quantity * UnitPrice;
}
