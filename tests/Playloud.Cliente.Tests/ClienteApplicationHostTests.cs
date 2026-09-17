using System.Threading;
using Playloud.Cliente.Bootstrap;
using Playloud.Cliente.Sales;

namespace Playloud.Cliente.Tests;

public sealed class ClienteApplicationHostTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"playloud-cliente-host-{Guid.NewGuid():N}.db");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public void CreateMainWindow_uses_runtime_checkout_as_data_context()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var host = ClienteApplicationHost.CreateAsync(
                    _databasePath,
                    cancellation.Token).GetAwaiter().GetResult();

                var window = host.CreateMainWindow();

                Assert.Same(host.Checkout, window.DataContext);
                Assert.IsType<SaleCheckoutViewModel>(window.DataContext);

                window.Close();
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }
}
