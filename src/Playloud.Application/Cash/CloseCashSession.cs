using Playloud.Domain.Cash;
using Playloud.Domain.Common;

namespace Playloud.Application.Cash;

public sealed record CloseCashSessionCommand(
    EntityId<CashSession> CashSessionId,
    decimal ActualCash);

public interface ICashSessionCloser
{
    Task<CashClosing> CloseAsync(
        EntityId<CashSession> cashSessionId,
        decimal actualCash,
        CancellationToken cancellationToken = default);
}

public interface ICloseCashSession
{
    Task<CashClosing> ExecuteAsync(
        CloseCashSessionCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class CloseCashSession(
    ICashSessionCloser closer) : ICloseCashSession
{
    public Task<CashClosing> ExecuteAsync(
        CloseCashSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ActualCash < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "Actual cash cannot be negative.");
        }

        return closer.CloseAsync(
            command.CashSessionId,
            command.ActualCash,
            cancellationToken);
    }
}
