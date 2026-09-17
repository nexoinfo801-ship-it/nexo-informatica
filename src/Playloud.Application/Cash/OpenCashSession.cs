using Playloud.Domain.Cash;
using Playloud.Domain.Common;

namespace Playloud.Application.Cash;

public sealed record ActiveCashSession(
    EntityId<CashSession> Id,
    DateOnly BusinessDate,
    decimal OpeningBalance);

public sealed record OpenCashSessionCommand(
    DateOnly BusinessDate,
    decimal OpeningBalance);

public interface IActiveCashSessionStore
{
    Task<ActiveCashSession> OpenOrGetAsync(
        DateOnly businessDate,
        decimal openingBalance,
        CancellationToken cancellationToken = default);
}

public interface IOpenCashSession
{
    Task<ActiveCashSession> ExecuteAsync(
        OpenCashSessionCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class OpenCashSession(
    IActiveCashSessionStore store) : IOpenCashSession
{
    public Task<ActiveCashSession> ExecuteAsync(
        OpenCashSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.OpeningBalance < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "Opening balance cannot be negative.");
        }

        return store.OpenOrGetAsync(
            command.BusinessDate,
            command.OpeningBalance,
            cancellationToken);
    }
}
