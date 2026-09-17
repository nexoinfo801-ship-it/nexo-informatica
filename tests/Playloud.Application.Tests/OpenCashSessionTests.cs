using Playloud.Application.Cash;
using Playloud.Domain.Cash;
using Playloud.Domain.Common;

namespace Playloud.Application.Tests;

public sealed class OpenCashSessionTests
{
    [Fact]
    public async Task Opens_cash_session_for_business_date()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var date = new DateOnly(2026, 9, 17);
        var expected = new ActiveCashSession(
            EntityId<CashSession>.New(),
            date,
            150m);
        var store = new FakeActiveCashSessionStore(expected);
        var useCase = new OpenCashSession(store);

        var result = await useCase.ExecuteAsync(
            new OpenCashSessionCommand(date, 150m),
            cancellationToken);

        Assert.Equal(expected, result);
        Assert.Equal(date, store.LastBusinessDate);
        Assert.Equal(150m, store.LastOpeningBalance);
        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public async Task Negative_opening_balance_is_rejected_before_storage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeActiveCashSessionStore(
            new ActiveCashSession(
                EntityId<CashSession>.New(),
                new DateOnly(2026, 9, 17),
                0m));
        var useCase = new OpenCashSession(store);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            useCase.ExecuteAsync(
                new OpenCashSessionCommand(new DateOnly(2026, 9, 17), -0.01m),
                cancellationToken));

        Assert.Equal(0, store.CallCount);
    }

    private sealed class FakeActiveCashSessionStore(ActiveCashSession result)
        : IActiveCashSessionStore
    {
        public int CallCount { get; private set; }

        public DateOnly? LastBusinessDate { get; private set; }

        public decimal? LastOpeningBalance { get; private set; }

        public Task<ActiveCashSession> OpenOrGetAsync(
            DateOnly businessDate,
            decimal openingBalance,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastBusinessDate = businessDate;
            LastOpeningBalance = openingBalance;
            return Task.FromResult(result);
        }
    }
}
