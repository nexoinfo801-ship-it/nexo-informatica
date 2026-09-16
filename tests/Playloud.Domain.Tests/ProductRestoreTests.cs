using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Domain.Tests;

public sealed class ProductRestoreTests
{
    [Fact]
    public void Restore_preserves_stable_product_identity_and_values()
    {
        var id = EntityId<Product>.New();

        var product = Product.Restore(id, "Café", 12.50m, 6m);

        Assert.Equal(id, product.Id);
        Assert.Equal("Café", product.Name);
        Assert.Equal(12.50m, product.UnitPrice);
        Assert.Equal(6m, product.UnitCost);
    }

    [Fact]
    public void Restore_reuses_create_validation_rules()
    {
        var id = EntityId<Product>.New();

        Assert.Throws<ArgumentException>(() => Product.Restore(id, " ", 1m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Product.Restore(id, "Produto", -1m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Product.Restore(id, "Produto", 1m, -1m));
    }
}
