using System.Threading;
using System.Windows.Controls;
using Playloud.Application.Sales;
using Playloud.Cliente.Sales;
using Playloud.Cliente.Shell;
using Playloud.Domain.Cash;
using Playloud.Domain.Common;
using Playloud.Domain.Sales;

namespace Playloud.Cliente.Tests;

public sealed class MainWindowCompositionTests
{
    [Fact]
    public void MainWindow_uses_injected_checkout_view_model_as_data_context()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewModel = new SaleCheckoutViewModel(new StubFinalizarVenda());
                var window = new MainWindow(viewModel);

                Assert.Same(viewModel, window.DataContext);
                window.Close();
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

    [Fact]
    public void MainWindow_exposes_commercial_shell_regions()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewModel = new SaleCheckoutViewModel(new StubFinalizarVenda());
                var window = new MainWindow(viewModel);

                Assert.NotNull(window.FindName("NavigationRail"));
                Assert.NotNull(window.FindName("OperationalHeader"));
                Assert.NotNull(window.FindName("SalesWorkspace"));
                Assert.NotNull(window.FindName("CheckoutStatusCard"));
                Assert.NotNull(window.FindName("ProductSearchBox"));
                Assert.NotNull(window.FindName("ProductSearchResults"));
                Assert.NotNull(window.FindName("SaleCartItems"));
                Assert.NotNull(window.FindName("CartSubtotal"));
                Assert.NotNull(window.FindName("ProductRegistrationPanel"));
                Assert.NotNull(window.FindName("NewProductNameBox"));
                Assert.NotNull(window.FindName("CreateProductButton"));
                Assert.NotNull(window.FindName("ProductManagementPanel"));
                Assert.NotNull(window.FindName("UpdateProductButton"));
                Assert.NotNull(window.FindName("AdjustStockButton"));

                var productTitle = Assert.IsType<TextBlock>(window.FindName("ProductTitle"));
                Assert.Equal("Playloud PDV & ERP", productTitle.Text);

                window.Close();
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

    private sealed class StubFinalizarVenda : IFinalizarVenda
    {
        public Task<FinalizarVendaResult> ExecuteAsync(
            FinalizarVendaCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FinalizarVendaResult(
                EntityId<Sale>.New(),
                0m,
                []));
    }
}
