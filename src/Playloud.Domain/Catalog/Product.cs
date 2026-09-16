using Playloud.Domain.Common;

namespace Playloud.Domain.Catalog;

public sealed class Product
{
    private Product(EntityId<Product> id, string name, decimal unitPrice, decimal unitCost)
    {
        Id = id;
        Name = name;
        UnitPrice = unitPrice;
        UnitCost = unitCost;
    }

    public EntityId<Product> Id { get; }

    public string Name { get; }

    public decimal UnitPrice { get; private set; }

    public decimal UnitCost { get; private set; }

    public static Product Create(string name, decimal unitPrice, decimal unitCost)
    {
        ValidateName(name);
        ValidateMoney(unitPrice, nameof(unitPrice));
        ValidateMoney(unitCost, nameof(unitCost));

        return new Product(EntityId<Product>.New(), name.Trim(), unitPrice, unitCost);
    }

    public static Product Restore(
        EntityId<Product> id,
        string name,
        decimal unitPrice,
        decimal unitCost)
    {
        ValidateName(name);
        ValidateMoney(unitPrice, nameof(unitPrice));
        ValidateMoney(unitCost, nameof(unitCost));

        return new Product(id, name.Trim(), unitPrice, unitCost);
    }

    public void Reprice(decimal unitPrice, decimal unitCost)
    {
        ValidateMoney(unitPrice, nameof(unitPrice));
        ValidateMoney(unitCost, nameof(unitCost));

        UnitPrice = unitPrice;
        UnitCost = unitCost;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product name is required.", nameof(name));
        }
    }

    private static void ValidateMoney(decimal value, string parameterName)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Money values cannot be negative.");
        }
    }
}
