using Playloud.Application.Sales;
using Playloud.Domain.Common;
using Playloud.Domain.Sales;

namespace Playloud.Cliente.Sales;

public sealed class SaleCheckoutViewModel(IFinalizarVenda finalizarVenda)
{
    private readonly IFinalizarVenda _finalizarVenda =
        finalizarVenda ?? throw new ArgumentNullException(nameof(finalizarVenda));

    public bool IsBusy { get; private set; }

    public string StatusMessage { get; private set; } = "Pronto para vender.";

    public EntityId<Sale>? LastSaleId { get; private set; }

    public decimal? LastTotal { get; private set; }

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
}
