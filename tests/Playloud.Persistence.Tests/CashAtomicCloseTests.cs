using Playloud.Domain.Cash;
using Playloud.Persistence.Sqlite;

namespace Playloud.Persistence.Tests;

public sealed class CashAtomicCloseTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"playloud-cash-close-{Guid.NewGuid():N}.db");

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
    public async Task Close_by_actual_cash_recalculates_expected_balance_inside_the_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var session = CashSession.Open(new DateOnly(2026, 10, 1), 100m);
        await store.SaveCashSessionAsync(session, cancellationToken);

        session.RegisterSupply(25m, "Troco adicional");
        await store.SaveCashAdjustmentAsync(
            session.Id,
            session.Movements[^1],
            cancellationToken);

        var closing = await store.CloseCashSessionAsync(
            session.Id,
            actualCash: 123m,
            cancellationToken);

        Assert.Equal(125m, closing.ExpectedCash);
        Assert.Equal(123m, closing.ActualCash);
        Assert.Equal(-2m, closing.Difference);
        Assert.False(await store.IsCashSessionOpenAsync(session.Id, cancellationToken));
    }

    [Fact]
    public async Task Close_by_actual_cash_rejects_negative_actual_cash_without_closing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var store = new SqliteCommerceStore(_databasePath);
        await store.InitializeAsync(cancellationToken);

        var session = CashSession.Open(new DateOnly(2026, 10, 1), 100m);
        await store.SaveCashSessionAsync(session, cancellationToken);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.CloseCashSessionAsync(
                session.Id,
                actualCash: -1m,
                cancellationToken));

        Assert.True(await store.IsCashSessionOpenAsync(session.Id, cancellationToken));
        Assert.Null(await store.GetCashClosingAsync(session.Id, cancellationToken));
    }
}
