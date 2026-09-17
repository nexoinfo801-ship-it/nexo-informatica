using Playloud.Cliente.Sales;
using Playloud.Cliente.Shell;

namespace Playloud.Cliente.Bootstrap;

public sealed class ClienteApplicationHost : IAsyncDisposable
{
    private readonly ClienteRuntime _runtime;

    private ClienteApplicationHost(ClienteRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public SaleCheckoutViewModel Checkout => _runtime.Checkout;

    public static async Task<ClienteApplicationHost> CreateAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var runtime = await ClienteRuntime.CreateAsync(databasePath, cancellationToken);
        return new ClienteApplicationHost(runtime);
    }

    public MainWindow CreateMainWindow() => new(Checkout);

    public ValueTask DisposeAsync() => _runtime.DisposeAsync();
}
