using Playloud.Application.Sales;
using Playloud.Cliente.Sales;
using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Payments;
using Playloud.Domain.Sales;

namespace Playloud.Cliente.Tests;

public sealed class SaleCheckoutViewModelTests
{
    [Fact]
    public async Task Successful_sale_exposes_commercial_success_state()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var saleId = EntityId<Sale>.New();
        var useCase = new FakeFinalizarVenda(new FinalizarVendaResult(
            saleId,
            25m,
            [new FinalizarVendaPayment(PaymentMethod.Cash, 25m)]));
        var viewModel = new SaleCheckoutViewModel(useCase);
        var command = CreateCommand();

        var completed = await viewModel.FinalizeAsync(command, cancellationToken);

        Assert.True(completed);
        Assert.Equal("Venda finalizada com sucesso.", viewModel.StatusMessage);
        Assert.Equal(saleId, viewModel.LastSaleId);
        Assert.Equal(25m, viewModel.LastTotal);
        Assert.False(viewModel.IsBusy);
        Assert.Same(command, useCase.LastCommand);
    }

    [Fact]
    public async Task Expected_business_failure_is_shown_without_false_success()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var useCase = new FakeFinalizarVenda(
            new InvalidOperationException("Requested quantity exceeds available stock."));
        var viewModel = new SaleCheckoutViewModel(useCase);

        var completed = await viewModel.FinalizeAsync(CreateCommand(), cancellationToken);

        Assert.False(completed);
        Assert.Equal(
            "Não foi possível finalizar a venda. Verifique os itens, o estoque e a forma de pagamento.",
            viewModel.StatusMessage);
        Assert.Null(viewModel.LastSaleId);
        Assert.Null(viewModel.LastTotal);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Unexpected_failure_is_not_swallowed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var useCase = new FakeFinalizarVenda(new IOException("disk failure"));
        var viewModel = new SaleCheckoutViewModel(useCase);

        await Assert.ThrowsAsync<IOException>(() =>
            viewModel.FinalizeAsync(CreateCommand(), cancellationToken));

        Assert.False(viewModel.IsBusy);
        Assert.Equal("Ocorreu uma falha inesperada durante a venda.", viewModel.StatusMessage);
    }

    private static FinalizarVendaCommand CreateCommand() => new(
        new DateOnly(2026, 9, 16),
        EntityId<CashSession>.New(),
        [new FinalizarVendaItem(EntityId<Product>.New(), 2m)],
        [new FinalizarVendaPayment(PaymentMethod.Cash, 25m)]);

    private sealed class FakeFinalizarVenda : IFinalizarVenda
    {
        private readonly FinalizarVendaResult? _result;
        private readonly Exception? _exception;

        public FakeFinalizarVenda(FinalizarVendaResult result) => _result = result;

        public FakeFinalizarVenda(Exception exception) => _exception = exception;

        public FinalizarVendaCommand? LastCommand { get; private set; }

        public Task<FinalizarVendaResult> ExecuteAsync(
            FinalizarVendaCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCommand = command;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result!);
        }
    }
}
