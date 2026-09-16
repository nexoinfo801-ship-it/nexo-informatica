using Playloud.Domain.Cash;

namespace Playloud.Domain.Tests;

public sealed class CashControlTests
{
    [Fact]
    public void Supply_increases_expected_physical_cash_and_is_auditable()
    {
        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 100m);

        session.RegisterSupply(50m, "Troco adicional");

        Assert.Equal(150m, session.ExpectedCashBalance);
        var movement = Assert.Single(session.Movements);
        Assert.Equal(CashMovementKind.Supply, movement.Kind);
        Assert.Equal(50m, movement.Amount);
        Assert.Equal("Troco adicional", movement.Reason);
        Assert.Null(movement.SaleId);
    }

    [Fact]
    public void Withdrawal_reduces_expected_physical_cash_and_is_auditable()
    {
        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 100m);

        session.RegisterWithdrawal(30m, "Sangria preventiva");

        Assert.Equal(70m, session.ExpectedCashBalance);
        var movement = Assert.Single(session.Movements);
        Assert.Equal(CashMovementKind.Withdrawal, movement.Kind);
        Assert.Equal(-30m, movement.Amount);
        Assert.Equal("Sangria preventiva", movement.Reason);
        Assert.Null(movement.SaleId);
    }

    [Fact]
    public void Withdrawal_cannot_make_expected_cash_negative()
    {
        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 20m);

        Assert.Throws<InvalidOperationException>(
            () => session.RegisterWithdrawal(21m, "Valor acima do disponível"));

        Assert.Equal(20m, session.ExpectedCashBalance);
        Assert.Empty(session.Movements);
    }

    [Fact]
    public void Manual_cash_movement_requires_reason()
    {
        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 20m);

        Assert.Throws<ArgumentException>(() => session.RegisterSupply(10m, "   "));
        Assert.Throws<ArgumentException>(() => session.RegisterWithdrawal(10m, string.Empty));
        Assert.Empty(session.Movements);
    }

    [Fact]
    public void Closed_cash_session_rejects_supply_and_withdrawal()
    {
        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 20m);
        _ = session.Close(actualCash: 20m);

        Assert.Throws<InvalidOperationException>(
            () => session.RegisterSupply(10m, "Tentativa após fechamento"));
        Assert.Throws<InvalidOperationException>(
            () => session.RegisterWithdrawal(10m, "Tentativa após fechamento"));
    }
}
