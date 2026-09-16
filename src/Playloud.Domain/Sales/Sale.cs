using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Payments;

namespace Playloud.Domain.Sales;

public sealed class Sale
{
    private readonly List<SaleLine> _lines = [];
    private readonly List<Payment> _payments = [];

    private Sale(DateOnly businessDate)
    {
        Id = EntityId<Sale>.New();
        BusinessDate = businessDate;
    }

    public EntityId<Sale> Id { get; }

    public DateOnly BusinessDate { get; }

    public IReadOnlyList<SaleLine> Lines => _lines;

    public IReadOnlyList<Payment> Payments => _payments;

    public decimal GrossTotal => _lines.Sum(static line => line.GrossTotal);

    public bool IsCompleted { get; private set; }

    public static Sale Start(DateOnly businessDate) => new(businessDate);

    public void AddLine(Product product, decimal quantity, decimal availableStock)
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("A completed sale cannot be changed.");
        }

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

    public void Complete(IEnumerable<Payment> payments)
    {
        ArgumentNullException.ThrowIfNull(payments);

        if (IsCompleted)
        {
            throw new InvalidOperationException("Sale is already completed.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A sale must contain at least one line.");
        }

        var paymentList = payments.ToList();
        var paidTotal = paymentList.Sum(static payment => payment.Amount);

        if (paidTotal != GrossTotal)
        {
            throw new InvalidOperationException("Payment total must match sale total.");
        }

        _payments.AddRange(paymentList);
        IsCompleted = true;
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
