using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Playloud.Application.Cash;
using Playloud.Application.Catalog;
using Playloud.Application.Sales;
using Playloud.Cliente.Common;
using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Payments;
using Playloud.Domain.Sales;

namespace Playloud.Cliente.Sales;

public sealed record PaymentMethodOption(PaymentMethod Method, string Label);

public sealed class SaleCheckoutViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<PaymentMethodOption> AvailablePaymentMethods =
    [
        new(PaymentMethod.Cash, "Dinheiro"),
        new(PaymentMethod.Pix, "Pix"),
        new(PaymentMethod.DebitCard, "Cartão de débito"),
        new(PaymentMethod.CreditCard, "Cartão de crédito")
    ];

    private readonly IFinalizarVenda _finalizarVenda;
    private readonly ISearchProducts _searchProducts;
    private readonly SaleCart _cart;
    private readonly ICreateProduct _createProduct;
    private readonly UpdateProduct _updateProduct;
    private readonly AdjustProductStock _adjustProductStock;
    private readonly IOpenCashSession _openCashSession;
    private readonly DateOnly _businessDate;
    private bool _isBusy;
    private string _statusMessage = "Pronto para vender.";
    private string _searchText = string.Empty;
    private string _newProductName = string.Empty;
    private decimal _newProductUnitPrice;
    private decimal _newProductUnitCost;
    private decimal _newProductOpeningStock;
    private EntityId<Product>? _managedProductId;
    private string _managedProductName = string.Empty;
    private decimal _managedProductUnitPrice;
    private decimal _managedProductUnitCost;
    private decimal _managedAvailableStock;
    private decimal _managedStockDelta;
    private string _managedStockReason = string.Empty;
    private EntityId<Sale>? _lastSaleId;
    private decimal? _lastTotal;
    private ActiveCashSession? _activeCashSession;
    private decimal _openingBalance;
    private PaymentMethodOption _selectedPaymentMethod = AvailablePaymentMethods[0];
    private decimal _amountReceived;
    private decimal _lastChangeDue;

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
        : this(
            finalizarVenda,
            searchProducts,
            cart,
            createProduct,
            new UpdateProduct(new EmptyProductCatalogManager()),
            new AdjustProductStock(new EmptyProductStockAdjuster()))
    {
    }

    public SaleCheckoutViewModel(
        IFinalizarVenda finalizarVenda,
        ISearchProducts searchProducts,
        SaleCart cart,
        IOpenCashSession openCashSession,
        TimeProvider timeProvider)
        : this(
            finalizarVenda,
            searchProducts,
            cart,
            new EmptyCreateProduct(),
            new UpdateProduct(new EmptyProductCatalogManager()),
            new AdjustProductStock(new EmptyProductStockAdjuster()),
            openCashSession,
            timeProvider)
    {
    }

    public SaleCheckoutViewModel(
        IFinalizarVenda finalizarVenda,
        ISearchProducts searchProducts,
        SaleCart cart,
        ICreateProduct createProduct,
        UpdateProduct updateProduct,
        AdjustProductStock adjustProductStock)
        : this(
            finalizarVenda,
            searchProducts,
            cart,
            createProduct,
            updateProduct,
            adjustProductStock,
            new UnavailableOpenCashSession(),
            TimeProvider.System)
    {
    }

    public SaleCheckoutViewModel(
        IFinalizarVenda finalizarVenda,
        ISearchProducts searchProducts,
        SaleCart cart,
        ICreateProduct createProduct,
        UpdateProduct updateProduct,
        AdjustProductStock adjustProductStock,
        IOpenCashSession openCashSession,
        TimeProvider timeProvider)
    {
        _finalizarVenda = finalizarVenda ?? throw new ArgumentNullException(nameof(finalizarVenda));
        _searchProducts = searchProducts ?? throw new ArgumentNullException(nameof(searchProducts));
        _cart = cart ?? throw new ArgumentNullException(nameof(cart));
        _createProduct = createProduct ?? throw new ArgumentNullException(nameof(createProduct));
        _updateProduct = updateProduct ?? throw new ArgumentNullException(nameof(updateProduct));
        _adjustProductStock =
            adjustProductStock ?? throw new ArgumentNullException(nameof(adjustProductStock));
        _openCashSession =
            openCashSession ?? throw new ArgumentNullException(nameof(openCashSession));
        ArgumentNullException.ThrowIfNull(timeProvider);
        _businessDate = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

        SearchCommand = new AsyncRelayCommand(() => SearchAsync());
        CreateProductCommand = new AsyncRelayCommand(() => CreateProductAsync());
        UpdateProductCommand = new AsyncRelayCommand(() => UpdateManagedProductAsync());
        AdjustStockCommand = new AsyncRelayCommand(() => AdjustManagedStockAsync());
        OpenCashCommand = new AsyncRelayCommand(() => OpenCashSessionAsync());
        FinalizeCurrentSaleCommand = new AsyncRelayCommand(() => FinalizeCurrentSaleAsync());
        SelectProductCommand = new RelayCommand(parameter =>
        {
            if (parameter is ProductSearchResult product)
            {
                SelectProduct(product);
            }
        });
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

    public ICommand UpdateProductCommand { get; }

    public ICommand AdjustStockCommand { get; }

    public ICommand OpenCashCommand { get; }

    public ICommand FinalizeCurrentSaleCommand { get; }

    public ICommand SelectProductCommand { get; }

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

    public bool HasManagedProduct => _managedProductId is not null;

    public string ManagedProductName
    {
        get => _managedProductName;
        set => SetField(ref _managedProductName, value ?? string.Empty);
    }

    public decimal ManagedProductUnitPrice
    {
        get => _managedProductUnitPrice;
        set => SetField(ref _managedProductUnitPrice, value);
    }

    public decimal ManagedProductUnitCost
    {
        get => _managedProductUnitCost;
        set => SetField(ref _managedProductUnitCost, value);
    }

    public decimal ManagedAvailableStock
    {
        get => _managedAvailableStock;
        private set => SetField(ref _managedAvailableStock, value);
    }

    public decimal ManagedStockDelta
    {
        get => _managedStockDelta;
        set => SetField(ref _managedStockDelta, value);
    }

    public string ManagedStockReason
    {
        get => _managedStockReason;
        set => SetField(ref _managedStockReason, value ?? string.Empty);
    }

    public decimal CartSubtotal => _cart.Subtotal;

    public bool HasCartItems => CartLines.Count > 0;

    public DateOnly BusinessDate => _businessDate;

    public IReadOnlyList<PaymentMethodOption> PaymentMethods => AvailablePaymentMethods;

    public decimal OpeningBalance
    {
        get => _openingBalance;
        set => SetField(ref _openingBalance, value);
    }

    public bool HasOpenCashSession => _activeCashSession is not null;

    public PaymentMethodOption SelectedPaymentMethod
    {
        get => _selectedPaymentMethod;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetField(ref _selectedPaymentMethod, value))
            {
                OnPropertyChanged(nameof(ChangeDue));
            }
        }
    }

    public decimal AmountReceived
    {
        get => _amountReceived;
        set
        {
            if (SetField(ref _amountReceived, value))
            {
                OnPropertyChanged(nameof(ChangeDue));
            }
        }
    }

    public decimal ChangeDue =>
        SelectedPaymentMethod.Method == PaymentMethod.Cash &&
        AmountReceived > CartSubtotal
            ? AmountReceived - CartSubtotal
            : 0m;

    public decimal LastChangeDue
    {
        get => _lastChangeDue;
        private set => SetField(ref _lastChangeDue, value);
    }

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

    public void SelectProduct(ProductSearchResult product)
    {
        ArgumentNullException.ThrowIfNull(product);

        _managedProductId = product.Id;
        ManagedProductName = product.Name;
        ManagedProductUnitPrice = product.UnitPrice;
        ManagedProductUnitCost = product.UnitCost;
        ManagedAvailableStock = product.AvailableStock;
        ManagedStockDelta = 0m;
        ManagedStockReason = string.Empty;
        OnPropertyChanged(nameof(HasManagedProduct));
        StatusMessage = "Produto selecionado para gerenciamento.";
    }

    public async Task<bool> UpdateManagedProductAsync(
        CancellationToken cancellationToken = default)
    {
        if (_managedProductId is not EntityId<Product> productId)
        {
            StatusMessage = "Selecione um produto para editar.";
            return false;
        }

        IsBusy = true;
        StatusMessage = "Atualizando produto...";

        try
        {
            var result = await _updateProduct.ExecuteAsync(
                new UpdateProductCommand(
                    productId,
                    ManagedProductName,
                    ManagedProductUnitPrice,
                    ManagedProductUnitCost),
                cancellationToken);

            SearchText = result.Name;
            await SearchAsync(cancellationToken);
            var refreshed = SearchResults.FirstOrDefault(product => product.Id == productId);
            if (refreshed is not null)
            {
                SelectProduct(refreshed);
            }

            StatusMessage = "Produto atualizado com sucesso.";
            return true;
        }
        catch (InvalidOperationException)
        {
            StatusMessage = "Já existe outro produto com este nome.";
            return false;
        }
        catch (ArgumentException)
        {
            StatusMessage = "Revise nome, preço e custo do produto.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> AdjustManagedStockAsync(
        CancellationToken cancellationToken = default)
    {
        if (_managedProductId is not EntityId<Product> productId)
        {
            StatusMessage = "Selecione um produto para ajustar o estoque.";
            return false;
        }

        IsBusy = true;
        StatusMessage = "Ajustando estoque...";

        try
        {
            var result = await _adjustProductStock.ExecuteAsync(
                new AdjustProductStockCommand(
                    productId,
                    ManagedStockDelta,
                    ManagedStockReason),
                cancellationToken);

            ManagedAvailableStock = result.CurrentStock;
            ManagedStockDelta = 0m;
            ManagedStockReason = string.Empty;
            SearchText = ManagedProductName;
            await SearchAsync(cancellationToken);
            var refreshed = SearchResults.FirstOrDefault(product => product.Id == productId);
            if (refreshed is not null)
            {
                SelectProduct(refreshed);
            }

            ManagedStockDelta = 0m;
            ManagedStockReason = string.Empty;
            StatusMessage = "Estoque ajustado com sucesso.";
            return true;
        }
        catch (InvalidOperationException)
        {
            StatusMessage = "O ajuste deixaria o estoque negativo.";
            return false;
        }
        catch (ArgumentException)
        {
            StatusMessage = "Informe uma quantidade diferente de zero e o motivo.";
            return false;
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

    public async Task<bool> OpenCashSessionAsync(
        CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = "Abrindo caixa...";

        try
        {
            var session = await _openCashSession.ExecuteAsync(
                new OpenCashSessionCommand(BusinessDate, OpeningBalance),
                cancellationToken);
            _activeCashSession = session;
            OpeningBalance = session.OpeningBalance;
            OnPropertyChanged(nameof(HasOpenCashSession));
            StatusMessage = "Caixa aberto e pronto para vendas.";
            return true;
        }
        catch (ArgumentException)
        {
            StatusMessage = "O saldo inicial do caixa não pode ser negativo.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> FinalizeCurrentSaleAsync(
        CancellationToken cancellationToken = default)
    {
        if (_activeCashSession is not ActiveCashSession session)
        {
            StatusMessage = "Abra o caixa antes de finalizar a venda.";
            return false;
        }

        if (_cart.Lines.Count == 0)
        {
            StatusMessage = "Adicione ao menos um produto à venda.";
            return false;
        }

        if (SelectedPaymentMethod.Method == PaymentMethod.Cash &&
            AmountReceived < CartSubtotal)
        {
            StatusMessage = "O valor recebido é menor que o total da venda.";
            return false;
        }

        var changeDue = ChangeDue;
        var command = new FinalizarVendaCommand(
            session.BusinessDate,
            session.Id,
            _cart.Lines
                .Select(static line => new FinalizarVendaItem(
                    line.ProductId,
                    line.Quantity))
                .ToArray(),
            [
                new FinalizarVendaPayment(
                    SelectedPaymentMethod.Method,
                    CartSubtotal)
            ]);

        if (!await FinalizeAsync(command, cancellationToken))
        {
            return false;
        }

        LastChangeDue = changeDue;
        _cart.Clear();
        RefreshCart();
        AmountReceived = 0m;
        return true;
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
        OnPropertyChanged(nameof(ChangeDue));
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

    private sealed class UnavailableOpenCashSession : IOpenCashSession
    {
        public Task<ActiveCashSession> ExecuteAsync(
            OpenCashSessionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Cash opening is not configured.");
    }

    private sealed class EmptyProductCatalogManager : IProductCatalogManager
    {
        public Task UpdateAsync(
            Product product,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Product management is not configured.");
    }

    private sealed class EmptyProductStockAdjuster : IProductStockAdjuster
    {
        public Task<decimal> AdjustAsync(
            EntityId<Product> productId,
            decimal quantityDelta,
            string reason,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Stock adjustment is not configured.");
    }

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
