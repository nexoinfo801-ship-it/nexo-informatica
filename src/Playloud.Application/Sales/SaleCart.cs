using Playloud.Application.Catalog;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Application.Sales;

public sealed record SaleCartLine(
    EntityId<Product> ProductId,
    string ProductName,
    decimal UnitPrice,
    decimal AvailableStock,
    decimal Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed class SaleCart
{
    private readonly List<SaleCartLine> _lines = [];

    public IReadOnlyList<SaleCartLine> Lines => _lines;

    public decimal Subtotal => _lines.Sum(static line => line.LineTotal);

    public void AddProduct(ProductSearchResult product, decimal quantity = 1m)
    {
        ArgumentNullException.ThrowIfNull(product);
        ValidateProduct(product);
        ValidateQuantity(quantity);

        var index = _lines.FindIndex(line => line.ProductId == product.Id);
        var newQuantity = quantity;
        if (index >= 0)
        {
            newQuantity += _lines[index].Quantity;
        }

        EnsureStock(newQuantity, product.AvailableStock);

        var newLine = new SaleCartLine(
            product.Id,
            product.Name,
            product.UnitPrice,
            product.AvailableStock,
            newQuantity);

        if (index >= 0)
        {
            _lines[index] = newLine;
        }
        else
        {
            _lines.Add(newLine);
        }
    }

    public void ChangeQuantity(EntityId<Product> productId, decimal quantity)
    {
        ValidateQuantity(quantity);

        var index = _lines.FindIndex(line => line.ProductId == productId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Product '{productId}' is not in the cart.");
        }

        var current = _lines[index];
        EnsureStock(quantity, current.AvailableStock);
        _lines[index] = current with { Quantity = quantity };
    }

    public bool RemoveProduct(EntityId<Product> productId) =>
        _lines.RemoveAll(line => line.ProductId == productId) > 0;

    public void Clear() => _lines.Clear();

    private static void ValidateProduct(ProductSearchResult product)
    {
        if (string.IsNullOrWhiteSpace(product.Name))
        {
            throw new ArgumentException("Product name is required.", nameof(product));
        }

        if (product.UnitPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(product), "Unit price cannot be negative.");
        }

        if (product.AvailableStock < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(product), "Available stock cannot be negative.");
        }
    }

    private static void ValidateQuantity(decimal quantity)
    {
        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }
    }

    private static void EnsureStock(decimal quantity, decimal availableStock)
    {
        if (quantity > availableStock)
        {
            throw new InvalidOperationException("Requested quantity exceeds available stock.");
        }
    }
}
