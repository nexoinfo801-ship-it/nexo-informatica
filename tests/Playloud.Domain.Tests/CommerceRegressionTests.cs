using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Inventory;
using Playloud.Domain.Sales;

namespace Playloud.Domain.Tests;

public sealed class CommerceRegressionTests
{
    [Fact]
    public void Product_identity_is_stable_when_other_products_are_removed()
    {
        var first = Product.Create("Café", 12.50m, 7.00m);
        var second = Product.Create("Pão", 5.00m, 2.00m);
        var secondId = second.Id;

        var catalog = new ProductCatalog([first, second]);
        catalog.Remove(first.Id);

        Assert.Equal(secondId, catalog.Get(secondId).Id);
    }

    [Fact]
    public void Stock_can_never_become_negative()
    {
        var product = Product.Create("Água", 4.00m, 1.50m);
        var stock = new StockItem(product.Id, 3m);

        Assert.Throws<InvalidOperationException>(() => stock.Remove(4m));
        Assert.Equal(3m, stock.Quantity);
    }

    [Fact]
    public void Sale_rejects_quantity_above_available_stock()
    {
        var product = Product.Create("Refrigerante", 8.00m, 3.50m);
        var sale = Sale.Start(DateOnly.FromDateTime(new DateTime(2026, 9, 16)));

        Assert.Throws<InvalidOperationException>(() => sale.AddLine(product, quantity: 6m, availableStock: 5m));
        Assert.Empty(sale.Lines);
    }

    [Fact]
    public void Sale_line_preserves_price_and_cost_snapshot_after_product_repricing()
    {
        var product = Product.Create("Marmita", 25.00m, 14.00m);
        var sale = Sale.Start(new DateOnly(2026, 9, 16));
        sale.AddLine(product, quantity: 2m, availableStock: 10m);

        product.Reprice(30.00m, 16.00m);

        var line = Assert.Single(sale.Lines);
        Assert.Equal(25.00m, line.UnitPrice);
        Assert.Equal(14.00m, line.UnitCost);
        Assert.Equal(50.00m, line.GrossTotal);
    }

    [Fact]
    public void Zero_percent_discount_is_valid_and_remains_zero()
    {
        var discount = DiscountPercent.Create(0m);

        Assert.Equal(0m, discount.Value);
        Assert.Equal(100m, discount.ApplyTo(100m));
    }

    [Fact]
    public void Business_date_uses_explicit_business_offset_not_utc_calendar_day()
    {
        var instant = new DateTimeOffset(2026, 9, 17, 1, 30, 0, TimeSpan.Zero);

        var businessDate = BusinessDate.FromInstant(instant, TimeSpan.FromHours(-3));

        Assert.Equal(new DateOnly(2026, 9, 16), businessDate.Value);
    }
}
