using System.Windows;
using Playloud.Cliente.Bootstrap;

namespace Playloud.Cliente;

public partial class App : System.Windows.Application
{
    private ClienteApplicationHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var paths = ClienteStoragePaths.CreateDefault();
            paths.EnsureDirectories();

            _host = await ClienteApplicationHost.CreateAsync(paths.DatabasePath);

            var window = _host.CreateMainWindow();
            MainWindow = window;
            window.Show();
        }
        catch
        {
            if (_host is not null)
            {
                await _host.DisposeAsync();
                _host = null;
            }

            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _host = null;
            }
        }
        finally
        {
            base.OnExit(e);
        }
    }
}
