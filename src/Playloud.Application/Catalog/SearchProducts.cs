using Playloud.Domain.Catalog;
using Playloud.Domain.Common;

namespace Playloud.Application.Catalog;

public sealed record ProductSearchResult(
    EntityId<Product> Id,
    string Name,
    decimal UnitPrice,
    decimal AvailableStock,
    decimal UnitCost = 0m);

public interface IProductSearchReader
{
    Task<IReadOnlyList<ProductSearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default);
}

public interface ISearchProducts
{
    Task<IReadOnlyList<ProductSearchResult>> ExecuteAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default);
}

public sealed class SearchProducts(IProductSearchReader reader) : ISearchProducts
{
    private readonly IProductSearchReader _reader =
        reader ?? throw new ArgumentNullException(nameof(reader));

    public Task<IReadOnlyList<ProductSearchResult>> ExecuteAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Search result limit must be between 1 and 100.");
        }

        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<ProductSearchResult>>([]);
        }

        return _reader.SearchAsync(normalizedQuery, limit, cancellationToken);
    }
}
