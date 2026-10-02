using Playloud.Application.Cash;
using Playloud.Domain.Cash;
using Playloud.Domain.Common;

namespace Playloud.Application.Tests;

public sealed class CloseCashSessionTests
{
    [Fact]
    public async Task Close_uses_session_id_and_actual_cash_and_returns_ledger_result()
    {
        var sessionId = EntityId<CashSession>.New();
        var closer = new RecordingCashSessionCloser(
            new CashClosing(ExpectedCash: 125m, ActualCash: 123m, Difference: -2m));
        var useCase = new CloseCashSession(closer);

        var result = await useCase.ExecuteAsync(
            new CloseCashSessionCommand(sessionId, ActualCash: 123m),
            TestContext.Current.CancellationToken);

        Assert.Equal(125m, result.ExpectedCash);
        Assert.Equal(123m, result.ActualCash);
        Assert.Equal(-2m, result.Difference);
        Assert.Equal(sessionId, closer.LastSessionId);
        Assert.Equal(123m, closer.LastActualCash);
    }

    [Fact]
    public async Task Close_rejects_negative_actual_cash_before_persistence()
    {
        var closer = new RecordingCashSessionCloser(
            new CashClosing(ExpectedCash: 0m, ActualCash: 0m, Difference: 0m));
        var useCase = new CloseCashSession(closer);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => useCase.ExecuteAsync(
                new CloseCashSessionCommand(
                    EntityId<CashSession>.New(),
                    ActualCash: -1m),
                TestContext.Current.CancellationToken));

        Assert.Null(closer.LastSessionId);
    }

    private sealed class RecordingCashSessionCloser(CashClosing result)
        : ICashSessionCloser
    {
        public EntityId<CashSession>? LastSessionId { get; private set; }

        public decimal? LastActualCash { get; private set; }

        public Task<CashClosing> CloseAsync(
            EntityId<CashSession> cashSessionId,
            decimal actualCash,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSessionId = cashSessionId;
            LastActualCash = actualCash;
            return Task.FromResult(result);
        }
    }
}
