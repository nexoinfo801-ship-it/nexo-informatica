using Playloud.Domain.Common;

namespace Playloud.Domain.Catalog;

public sealed class ProductCatalog
{
    private readonly Dictionary<EntityId<Product>, Product> _products;

    public ProductCatalog(IEnumerable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        _products = products.ToDictionary(product => product.Id);
    }

    public Product Get(EntityId<Product> id)
    {
        if (!_products.TryGetValue(id, out var product))
        {
            throw new KeyNotFoundException($"Product '{id}' was not found.");
        }

        return product;
    }

    public bool Remove(EntityId<Product> id) => _products.Remove(id);
}
