using Playloud.Application.Cash;
using Playloud.Domain.Cash;
using Playloud.Persistence.Sqlite;

namespace Playloud.Persistence.Tests;

public sealed class CashCloseAdapterTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"playloud-cash-adapter-{Guid.NewGuid():N}.db");

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
    public async Task Adapter_implements_cash_closer_and_returns_persisted_difference()
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

        ICashSessionCloser closer = new SqliteCommerceAdapter(store);

        var closing = await closer.CloseAsync(
            session.Id,
            actualCash: 123m,
            cancellationToken);

        Assert.Equal(125m, closing.ExpectedCash);
        Assert.Equal(123m, closing.ActualCash);
        Assert.Equal(-2m, closing.Difference);
        Assert.False(await store.IsCashSessionOpenAsync(session.Id, cancellationToken));
    }
}
