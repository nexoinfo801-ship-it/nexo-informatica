using Playloud.Domain.Common;
using Playloud.Domain.Payments;
using Playloud.Domain.Sales;

namespace Playloud.Domain.Cash;

public sealed class CashSession
{
    private readonly List<CashMovement> _movements = [];
    private readonly HashSet<EntityId<Sale>> _registeredSales = [];

    private CashSession(DateOnly businessDate, decimal openingBalance)
    {
        Id = EntityId<CashSession>.New();
        BusinessDate = businessDate;
        OpeningBalance = openingBalance;
        IsOpen = true;
    }

    public EntityId<CashSession> Id { get; }

    public DateOnly BusinessDate { get; }

    public decimal OpeningBalance { get; }

    public bool IsOpen { get; private set; }

    public IReadOnlyList<CashMovement> Movements => _movements;

    public decimal ExpectedCashBalance => OpeningBalance + _movements.Sum(static movement => movement.Amount);

    public static CashSession Open(DateOnly businessDate, decimal openingBalance)
    {
        if (openingBalance < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(openingBalance), "Opening balance cannot be negative.");
        }

        return new CashSession(businessDate, openingBalance);
    }

    public void RegisterSale(Sale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        EnsureOpen();

        if (!sale.IsCompleted)
        {
            throw new InvalidOperationException("Only completed sales can be registered in cash.");
        }

        if (sale.BusinessDate != BusinessDate)
        {
            throw new InvalidOperationException("Sale business date does not match cash session date.");
        }

        if (_registeredSales.Contains(sale.Id))
        {
            throw new InvalidOperationException("Sale is already registered in this cash session.");
        }

        foreach (var payment in sale.Payments)
        {
            if (payment.Method != PaymentMethod.Cash)
            {
                continue;
            }

            _movements.Add(new CashMovement(
                EntityId<CashMovement>.New(),
                sale.Id,
                payment.Amount));
        }

        _registeredSales.Add(sale.Id);
    }

    public CashClosing Close(decimal actualCash)
    {
        EnsureOpen();

        if (actualCash < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(actualCash), "Actual cash cannot be negative.");
        }

        var expected = ExpectedCashBalance;
        IsOpen = false;
        return new CashClosing(expected, actualCash, actualCash - expected);
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("Cash session is closed.");
        }
    }
}

public sealed record CashMovement(
    EntityId<CashMovement> Id,
    EntityId<Sale> SaleId,
    decimal Amount);

public sealed record CashClosing(
    decimal ExpectedCash,
    decimal ActualCash,
    decimal Difference);
