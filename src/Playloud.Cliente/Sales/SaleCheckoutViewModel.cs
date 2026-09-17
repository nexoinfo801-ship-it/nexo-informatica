using System.ComponentModel;
using System.Runtime.CompilerServices;
using Playloud.Application.Sales;
using Playloud.Domain.Common;
using Playloud.Domain.Sales;

namespace Playloud.Cliente.Sales;

public sealed class SaleCheckoutViewModel(IFinalizarVenda finalizarVenda) : INotifyPropertyChanged
{
    private readonly IFinalizarVenda _finalizarVenda =
        finalizarVenda ?? throw new ArgumentNullException(nameof(finalizarVenda));
    private bool _isBusy;
    private string _statusMessage = "Pronto para vender.";
    private EntityId<Sale>? _lastSaleId;
    private decimal? _lastTotal;

    public event PropertyChangedEventHandler? PropertyChanged;

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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
