using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Playloud.Application.Catalog;
using Playloud.Application.Sales;
using Playloud.Cliente.Common;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Sales;

namespace Playloud.Cliente.Sales;

public sealed class SaleCheckoutViewModel : INotifyPropertyChanged
{
    private readonly IFinalizarVenda _finalizarVenda;
    private readonly ISearchProducts _searchProducts;
    private readonly SaleCart _cart;
    private bool _isBusy;
    private string _statusMessage = "Pronto para vender.";
    private string _searchText = string.Empty;
    private EntityId<Sale>? _lastSaleId;
    private decimal? _lastTotal;

    public SaleCheckoutViewModel(IFinalizarVenda finalizarVenda)
        : this(finalizarVenda, new EmptySearchProducts(), new SaleCart())
    {
    }

    public SaleCheckoutViewModel(
        IFinalizarVenda finalizarVenda,
        ISearchProducts searchProducts,
        SaleCart cart)
    {
        _finalizarVenda = finalizarVenda ?? throw new ArgumentNullException(nameof(finalizarVenda));
        _searchProducts = searchProducts ?? throw new ArgumentNullException(nameof(searchProducts));
        _cart = cart ?? throw new ArgumentNullException(nameof(cart));

        SearchCommand = new AsyncRelayCommand(() => SearchAsync());
        AddProductCommand = new RelayCommand(parameter =>
        {
            if (parameter is ProductSearchResult product)
            {
                AddProduct(product);
            }
        });
        IncreaseQuantityCommand = new RelayCommand(parameter =>
        {
            if (parameter is EntityId<Product> productId)
            {
                IncreaseQuantity(productId);
            }
        });
        DecreaseQuantityCommand = new RelayCommand(parameter =>
        {
            if (parameter is EntityId<Product> productId)
            {
                DecreaseQuantity(productId);
            }
        });
        RemoveProductCommand = new RelayCommand(parameter =>
        {
            if (parameter is EntityId<Product> productId)
            {
                RemoveProduct(productId);
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProductSearchResult> SearchResults { get; } = [];

    public ObservableCollection<SaleCartLine> CartLines { get; } = [];

    public ICommand SearchCommand { get; }

    public ICommand AddProductCommand { get; }

    public ICommand IncreaseQuantityCommand { get; }

    public ICommand DecreaseQuantityCommand { get; }

    public ICommand RemoveProductCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value ?? string.Empty);
    }

    public decimal CartSubtotal => _cart.Subtotal;

    public bool HasCartItems => CartLines.Count > 0;

    public EntityId<Sale>? LastSaleId
    {
        get => _lastSaleId;
        private set => SetField(ref _lastSaleId, value);
    }

    public decimal? LastTotal
    {
        get => _lastTotal;
        private set => SetField(ref _lastTotal, value);
    }

    public async Task SearchAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = "Buscando produtos...";

        try
        {
            var products = await _searchProducts.ExecuteAsync(
                SearchText,
                cancellationToken: cancellationToken);

            SearchResults.Clear();
            foreach (var product in products)
            {
                SearchResults.Add(product);
            }

            StatusMessage = products.Count switch
            {
                0 => "Nenhum produto encontrado.",
                1 => "1 produto encontrado.",
                _ => $"{products.Count} produtos encontrados."
            };
        }
        catch (ArgumentException)
        {
            SearchResults.Clear();
            StatusMessage = "Digite um nome de produto válido para buscar.";
        }
        catch
        {
            StatusMessage = "Ocorreu uma falha inesperada durante a busca.";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void AddProduct(ProductSearchResult product)
    {
        ArgumentNullException.ThrowIfNull(product);

        try
        {
            _cart.AddProduct(product);
            RefreshCart();
            StatusMessage = $"{product.Name} adicionado à venda.";
        }
        catch (InvalidOperationException)
        {
            StatusMessage = "Estoque insuficiente para aumentar este item.";
        }
    }

    public void IncreaseQuantity(EntityId<Product> productId)
    {
        var line = _cart.Lines.Single(item => item.ProductId == productId);

        try
        {
            _cart.ChangeQuantity(productId, line.Quantity + 1m);
            RefreshCart();
            StatusMessage = "Quantidade atualizada.";
        }
        catch (InvalidOperationException)
        {
            StatusMessage = "Estoque insuficiente para aumentar este item.";
        }
    }

    public void DecreaseQuantity(EntityId<Product> productId)
    {
        var line = _cart.Lines.Single(item => item.ProductId == productId);
        if (line.Quantity <= 1m)
        {
            RemoveProduct(productId);
            return;
        }

        _cart.ChangeQuantity(productId, line.Quantity - 1m);
        RefreshCart();
        StatusMessage = "Quantidade atualizada.";
    }

    public void RemoveProduct(EntityId<Product> productId)
    {
        if (_cart.RemoveProduct(productId))
        {
            RefreshCart();
            StatusMessage = "Item removido da venda.";
        }
    }

    public async Task<bool> FinalizeAsync(
        FinalizarVendaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IsBusy = true;
        StatusMessage = "Finalizando venda...";
        LastSaleId = null;
        LastTotal = null;

        try
        {
            var result = await _finalizarVenda.ExecuteAsync(command, cancellationToken);
            LastSaleId = result.SaleId;
            LastTotal = result.GrossTotal;
            StatusMessage = "Venda finalizada com sucesso.";
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            KeyNotFoundException or
            ArgumentException)
        {
            StatusMessage =
                "Não foi possível finalizar a venda. Verifique os itens, o estoque e a forma de pagamento.";
            return false;
        }
        catch
        {
            StatusMessage = "Ocorreu uma falha inesperada durante a venda.";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshCart()
    {
        CartLines.Clear();
        foreach (var line in _cart.Lines)
        {
            CartLines.Add(line);
        }

        OnPropertyChanged(nameof(CartSubtotal));
        OnPropertyChanged(nameof(HasCartItems));
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class EmptySearchProducts : ISearchProducts
    {
        public Task<IReadOnlyList<ProductSearchResult>> ExecuteAsync(
            string query,
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ProductSearchResult>>([]);
        }
    }
}
