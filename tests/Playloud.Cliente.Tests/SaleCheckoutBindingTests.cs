using System.ComponentModel;
using Playloud.Application.Sales;
using Playloud.Cliente.Sales;
using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Payments;
using Playloud.Domain.Sales;

namespace Playloud.Cliente.Tests;

public sealed class SaleCheckoutBindingTests
{
    [Fact]
    public async Task Checkout_view_model_notifies_bindable_state_changes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var saleId = EntityId<Sale>.New();
        var viewModel = new SaleCheckoutViewModel(new SuccessfulFinalizarVenda(saleId));
        var notifier = Assert.IsAssignableFrom<INotifyPropertyChanged>(viewModel);
        var changed = new List<string?>();
        notifier.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await viewModel.FinalizeAsync(
            new FinalizarVendaCommand(
                new DateOnly(2026, 9, 16),
                EntityId<CashSession>.New(),
                [new FinalizarVendaItem(EntityId<Product>.New(), 2m)],
                [new FinalizarVendaPayment(PaymentMethod.Cash, 25m)]),
            cancellationToken);

        Assert.Contains(nameof(SaleCheckoutViewModel.IsBusy), changed);
        Assert.Contains(nameof(SaleCheckoutViewModel.StatusMessage), changed);
        Assert.Contains(nameof(SaleCheckoutViewModel.LastSaleId), changed);
        Assert.Contains(nameof(SaleCheckoutViewModel.LastTotal), changed);
    }

    private sealed class SuccessfulFinalizarVenda(EntityId<Sale> saleId) : IFinalizarVenda
    {
        public Task<FinalizarVendaResult> ExecuteAsync(
            FinalizarVendaCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new FinalizarVendaResult(
                saleId,
                25m,
                command.Payments));
        }
    }
}
