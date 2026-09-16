using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Payments;
using Playloud.Domain.Sales;

namespace Playloud.Domain.Tests;

public sealed class CashPaymentTests
{
    [Fact]
    public void Cash_sale_increases_physical_cash_balance()
    {
        var sale = CreateSale(totalUnitPrice: 30m);
        sale.Complete([Payment.Create(PaymentMethod.Cash, 30m)]);
        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 100m);

        session.RegisterSale(sale);

        Assert.Equal(130m, session.ExpectedCashBalance);
        Assert.Single(session.Movements);
        Assert.Equal(30m, session.Movements[0].Amount);
    }

    [Fact]
    public void Pix_sale_does_not_change_physical_cash_balance()
    {
        var sale = CreateSale(totalUnitPrice: 30m);
        sale.Complete([Payment.Create(PaymentMethod.Pix, 30m)]);
        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 100m);

        session.RegisterSale(sale);

        Assert.Equal(100m, session.ExpectedCashBalance);
        Assert.Empty(session.Movements);
    }

    [Fact]
    public void Mixed_payment_changes_cash_only_by_cash_portion()
    {
        var sale = CreateSale(totalUnitPrice: 50m);
        sale.Complete([
            Payment.Create(PaymentMethod.Cash, 20m),
            Payment.Create(PaymentMethod.CreditCard, 30m)
        ]);
        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 80m);

        session.RegisterSale(sale);

        Assert.Equal(100m, session.ExpectedCashBalance);
        Assert.Single(session.Movements);
        Assert.Equal(20m, session.Movements[0].Amount);
    }

    [Fact]
    public void Closing_computes_expected_actual_and_difference()
    {
        var sale = CreateSale(totalUnitPrice: 25m);
        sale.Complete([Payment.Create(PaymentMethod.Cash, 25m)]);
        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 50m);
        session.RegisterSale(sale);

        var closing = session.Close(actualCash: 74m);

        Assert.Equal(75m, closing.ExpectedCash);
        Assert.Equal(74m, closing.ActualCash);
        Assert.Equal(-1m, closing.Difference);
    }

    [Fact]
    public void Cash_session_cannot_close_twice()
    {
        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 0m);
        _ = session.Close(actualCash: 0m);

        Assert.Throws<InvalidOperationException>(() => session.Close(actualCash: 0m));
    }

    [Fact]
    public void Same_sale_cannot_be_registered_twice()
    {
        var sale = CreateSale(totalUnitPrice: 10m);
        sale.Complete([Payment.Create(PaymentMethod.Cash, 10m)]);
        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 0m);
        session.RegisterSale(sale);

        Assert.Throws<InvalidOperationException>(() => session.RegisterSale(sale));
        Assert.Equal(10m, session.ExpectedCashBalance);
    }

    [Fact]
    public void Sale_cannot_complete_when_payments_do_not_match_total()
    {
        var sale = CreateSale(totalUnitPrice: 40m);

        Assert.Throws<InvalidOperationException>(
            () => sale.Complete([Payment.Create(PaymentMethod.Cash, 39m)]));
    }

    private static Sale CreateSale(decimal totalUnitPrice)
    {
        var product = Product.Create("Produto", totalUnitPrice, totalUnitPrice / 2m);
        var sale = Sale.Start(new DateOnly(2026, 9, 16));
        sale.AddLine(product, quantity: 1m, availableStock: 10m);
        return sale;
    }
}
