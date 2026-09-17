using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Application.Catalog;

public sealed record CreateProductCommand(
    string Name,
    decimal UnitPrice,
    decimal UnitCost,
    decimal OpeningStock);

public sealed record CreateProductResult(
    EntityId<Product> ProductId,
    string Name,
    decimal UnitPrice,
    decimal OpeningStock);

public interface IProductCatalogWriter
{
    Task SaveAsync(
        Product product,
        decimal openingStock,
        CancellationToken cancellationToken = default);
}

public interface ICreateProduct
{
    Task<CreateProductResult> ExecuteAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class CreateProduct(IProductCatalogWriter writer) : ICreateProduct
{
    private readonly IProductCatalogWriter _writer =
        writer ?? throw new ArgumentNullException(nameof(writer));

    public async Task<CreateProductResult> ExecuteAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.OpeningStock < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "Opening stock cannot be negative.");
        }

        var product = Product.Create(
            command.Name,
            command.UnitPrice,
            command.UnitCost);

        await _writer.SaveAsync(product, command.OpeningStock, cancellationToken);

        return new CreateProductResult(
            product.Id,
            product.Name,
            product.UnitPrice,
            command.OpeningStock);
    }
}
