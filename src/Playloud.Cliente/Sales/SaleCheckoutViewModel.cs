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
    private readonly ICreateProduct _createProduct;
    private bool _isBusy;
    private string _statusMessage = "Pronto para vender.";
    private string _searchText = string.Empty;
    private string _newProductName = string.Empty;
    private decimal _newProductUnitPrice;
    private decimal _newProductUnitCost;
    private decimal _newProductOpeningStock;
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
        : this(finalizarVenda, searchProducts, cart, new EmptyCreateProduct())
    {
    }

    public SaleCheckoutViewModel(
        IFinalizarVenda finalizarVenda,
        ISearchProducts searchProducts,
        SaleCart cart,
        ICreateProduct createProduct)
    {
        _finalizarVenda = finalizarVenda ?? throw new ArgumentNullException(nameof(finalizarVenda));
        _searchProducts = searchProducts ?? throw new ArgumentNullException(nameof(searchProducts));
        _cart = cart ?? throw new ArgumentNullException(nameof(cart));
        _createProduct = createProduct ?? throw new ArgumentNullException(nameof(createProduct));

        SearchCommand = new AsyncRelayCommand(() => SearchAsync());
        CreateProductCommand = new AsyncRelayCommand(() => CreateProductAsync());
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

    public ICommand CreateProductCommand { get; }

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

    public string NewProductName
    {
        get => _newProductName;
        set => SetField(ref _newProductName, value ?? string.Empty);
    }

    public decimal NewProductUnitPrice
    {
        get => _newProductUnitPrice;
        set => SetField(ref _newProductUnitPrice, value);
    }

    public decimal NewProductUnitCost
    {
        get => _newProductUnitCost;
        set => SetField(ref _newProductUnitCost, value);
    }

    public decimal NewProductOpeningStock
    {
        get => _newProductOpeningStock;
        set => SetField(ref _newProductOpeningStock, value);
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

    public async Task<bool> CreateProductAsync(
        CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = "Cadastrando produto...";

        try
        {
            var result = await _createProduct.ExecuteAsync(
                new Playloud.Application.Catalog.CreateProductCommand(
                    NewProductName,
                    NewProductUnitPrice,
                    NewProductUnitCost,
                    NewProductOpeningStock),
                cancellationToken);

            NewProductName = string.Empty;
            NewProductUnitPrice = 0m;
            NewProductUnitCost = 0m;
            NewProductOpeningStock = 0m;
            SearchText = result.Name;

            await SearchAsync(cancellationToken);
            StatusMessage = "Produto cadastrado e pronto para venda.";
            return true;
        }
        catch (InvalidOperationException)
        {
            StatusMessage = "Já existe um produto com este nome.";
            return false;
        }
        catch (ArgumentException)
        {
            StatusMessage = "Revise nome, preços e estoque inicial.";
            return false;
        }
        catch
        {
            StatusMessage = "Ocorreu uma falha inesperada durante o cadastro.";
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

    private sealed class EmptyCreateProduct : ICreateProduct
    {
        public Task<CreateProductResult> ExecuteAsync(
            Playloud.Application.Catalog.CreateProductCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Product registration is not configured.");
    }

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
