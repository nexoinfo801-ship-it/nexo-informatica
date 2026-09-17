using Playloud.Application.Catalog;
using Playloud.Application.Sales;
using Playloud.Cliente.Sales;
using Playloud.Persistence.Sqlite;

namespace Playloud.Cliente.Bootstrap;

public sealed class ClienteRuntime : IAsyncDisposable
{
    private readonly SqliteCommerceStore _store;

    private ClienteRuntime(
        SqliteCommerceStore store,
        SaleCheckoutViewModel checkout)
    {
        _store = store;
        Checkout = checkout;
    }

    public SaleCheckoutViewModel Checkout { get; }

    public static async Task<ClienteRuntime> CreateAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var store = new SqliteCommerceStore(databasePath);

        try
        {
            await store.InitializeAsync(cancellationToken);
            var adapter = new SqliteCommerceAdapter(store);
            IFinalizarVenda finalizarVenda = new FinalizarVenda(adapter, adapter);
            ISearchProducts searchProducts = new SearchProducts(adapter);
            var checkout = new SaleCheckoutViewModel(
                finalizarVenda,
                searchProducts,
                new SaleCart());
            return new ClienteRuntime(store, checkout);
        }
        catch
        {
            await store.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _store.DisposeAsync();
}
