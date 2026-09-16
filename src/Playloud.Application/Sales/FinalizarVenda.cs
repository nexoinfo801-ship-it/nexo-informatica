using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Payments;
using Playloud.Domain.Sales;

namespace Playloud.Application.Sales;

public sealed record ProductSnapshot(
    EntityId<Product> Id,
    string Name,
    decimal UnitPrice,
    decimal UnitCost,
    decimal AvailableStock);

public interface IProductSnapshotReader
{
    Task<ProductSnapshot?> ReadAsync(
        EntityId<Product> productId,
        CancellationToken cancellationToken = default);
}

public interface ISaleCommitter
{
    Task CommitSaleAsync(
        Sale sale,
        EntityId<CashSession> cashSessionId,
        CancellationToken cancellationToken = default);
}

public sealed record FinalizarVendaItem(EntityId<Product> ProductId, decimal Quantity);

public sealed record FinalizarVendaPayment(PaymentMethod Method, decimal Amount);

public sealed record FinalizarVendaCommand(
    DateOnly BusinessDate,
    EntityId<CashSession> CashSessionId,
    IReadOnlyList<FinalizarVendaItem> Items,
    IReadOnlyList<FinalizarVendaPayment> Payments);

public sealed record FinalizarVendaResult(
    EntityId<Sale> SaleId,
    decimal GrossTotal,
    IReadOnlyList<FinalizarVendaPayment> Payments);

public sealed class FinalizarVenda(
    IProductSnapshotReader productSnapshotReader,
    ISaleCommitter saleCommitter)
{
    public async Task<FinalizarVendaResult> ExecuteAsync(
        FinalizarVendaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sale = Sale.Start(command.BusinessDate);

        foreach (var item in command.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await productSnapshotReader.ReadAsync(item.ProductId, cancellationToken)
                ?? throw new KeyNotFoundException($"Product {item.ProductId} was not found.");

            var product = Product.Restore(
                snapshot.Id,
                snapshot.Name,
                snapshot.UnitPrice,
                snapshot.UnitCost);

            sale.AddLine(product, item.Quantity, snapshot.AvailableStock);
        }

        var payments = command.Payments
            .Select(static payment => Payment.Create(payment.Method, payment.Amount))
            .ToArray();

        sale.Complete(payments);
        await saleCommitter.CommitSaleAsync(sale, command.CashSessionId, cancellationToken);

        return new FinalizarVendaResult(
            sale.Id,
            sale.GrossTotal,
            command.Payments.ToArray());
    }
}
