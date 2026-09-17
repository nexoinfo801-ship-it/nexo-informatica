using System.Windows;
using Playloud.Cliente.Sales;

namespace Playloud.Cliente.Shell;

public partial class MainWindow : Window
{
    public MainWindow(SaleCheckoutViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }
}
