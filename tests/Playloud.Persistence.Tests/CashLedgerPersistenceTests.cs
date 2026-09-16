using Playloud.Domain.Cash;
using Playloud.Persistence.Sqlite;

namespace Playloud.Persistence.Tests;

public sealed class CashLedgerPersistenceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"playloud-cash-{Guid.NewGuid():N}.db");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Supply_and_withdrawal_are_persisted_with_audit_reason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 100m);
        await store.SaveCashSessionAsync(session, cancellationToken);

        session.RegisterSupply(40m, "Troco adicional");
        await store.SaveCashAdjustmentAsync(session.Id, session.Movements[^1], cancellationToken);

        session.RegisterWithdrawal(25m, "Sangria preventiva");
        await store.SaveCashAdjustmentAsync(session.Id, session.Movements[^1], cancellationToken);

        Assert.Equal(115m, await store.GetExpectedCashBalanceAsync(session.Id, cancellationToken));

        var audit = await store.GetCashAdjustmentsAsync(session.Id, cancellationToken);
        Assert.Equal(2, audit.Count);
        Assert.Equal(CashMovementKind.Supply, audit[0].Kind);
        Assert.Equal(40m, audit[0].Amount);
        Assert.Equal("Troco adicional", audit[0].Reason);
        Assert.Equal(CashMovementKind.Withdrawal, audit[1].Kind);
        Assert.Equal(-25m, audit[1].Amount);
        Assert.Equal("Sangria preventiva", audit[1].Reason);
    }

    [Fact]
    public async Task Closing_is_persisted_and_locks_future_manual_movements()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 100m);
        await store.SaveCashSessionAsync(session, cancellationToken);

        session.RegisterSupply(20m, "Troco adicional");
        await store.SaveCashAdjustmentAsync(session.Id, session.Movements[^1], cancellationToken);

        var closing = session.Close(actualCash: 119m);
        await store.CloseCashSessionAsync(session.Id, closing, cancellationToken);

        Assert.False(await store.IsCashSessionOpenAsync(session.Id, cancellationToken));
        var storedClosing = await store.GetCashClosingAsync(session.Id, cancellationToken);
        Assert.NotNull(storedClosing);
        Assert.Equal(120m, storedClosing.ExpectedCash);
        Assert.Equal(119m, storedClosing.ActualCash);
        Assert.Equal(-1m, storedClosing.Difference);

        var secondSession = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 0m);
        secondSession.RegisterSupply(1m, "Tentativa tardia");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveCashAdjustmentAsync(
                session.Id,
                secondSession.Movements[^1],
                cancellationToken));
    }

    [Fact]
    public async Task Closing_rejects_mismatched_expected_cash_and_keeps_session_open()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var session = CashSession.Open(new DateOnly(2026, 9, 16), openingBalance: 50m);
        await store.SaveCashSessionAsync(session, cancellationToken);

        var invalidClosing = new CashClosing(ExpectedCash: 55m, ActualCash: 55m, Difference: 0m);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CloseCashSessionAsync(session.Id, invalidClosing, cancellationToken));

        Assert.True(await store.IsCashSessionOpenAsync(session.Id, cancellationToken));
        Assert.Null(await store.GetCashClosingAsync(session.Id, cancellationToken));
    }
}
