using Playloud.Application.Catalog;
using Playloud.Application.Sales;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Application.Tests;

public sealed class SaleCartTests
{
    [Fact]
    public void Adding_the_same_product_aggregates_quantity_and_total()
    {
        var product = CreateProduct("Café", unitPrice: 12.50m, availableStock: 5m);
        var cart = new SaleCart();

        cart.AddProduct(product, 1m);
        cart.AddProduct(product, 2m);

        var line = Assert.Single(cart.Lines);
        Assert.Equal(3m, line.Quantity);
        Assert.Equal(37.50m, line.LineTotal);
        Assert.Equal(37.50m, cart.Subtotal);
    }

    [Fact]
    public void Cart_never_accepts_quantity_above_available_stock()
    {
        var product = CreateProduct("Produto", unitPrice: 10m, availableStock: 2m);
        var cart = new SaleCart();

        cart.AddProduct(product, 2m);

        Assert.Throws<InvalidOperationException>(() => cart.AddProduct(product, 1m));
        Assert.Equal(2m, Assert.Single(cart.Lines).Quantity);
    }

    [Fact]
    public void Quantity_can_be_changed_but_must_remain_positive_and_within_stock()
    {
        var product = CreateProduct("Produto", unitPrice: 8m, availableStock: 4m);
        var cart = new SaleCart();
        cart.AddProduct(product, 1m);

        cart.ChangeQuantity(product.Id, 3m);

        Assert.Equal(3m, Assert.Single(cart.Lines).Quantity);
        Assert.Throws<ArgumentOutOfRangeException>(() => cart.ChangeQuantity(product.Id, 0m));
        Assert.Throws<InvalidOperationException>(() => cart.ChangeQuantity(product.Id, 5m));
    }

    [Fact]
    public void Removing_a_product_is_safe_and_recalculates_subtotal()
    {
        var coffee = CreateProduct("Café", unitPrice: 12m, availableStock: 5m);
        var juice = CreateProduct("Suco", unitPrice: 7m, availableStock: 5m);
        var cart = new SaleCart();
        cart.AddProduct(coffee, 2m);
        cart.AddProduct(juice, 1m);

        var removed = cart.RemoveProduct(coffee.Id);

        Assert.True(removed);
        Assert.False(cart.RemoveProduct(coffee.Id));
        Assert.Equal(7m, cart.Subtotal);
        Assert.Equal(juice.Id, Assert.Single(cart.Lines).ProductId);
    }

    private static ProductSearchResult CreateProduct(
        string name,
        decimal unitPrice,
        decimal availableStock) =>
        new(EntityId<Product>.New(), name, unitPrice, availableStock);
}
