using WalletApp.Domain;

namespace WalletApp.Application.Abstractions;

/// <summary>
/// Transport a withdrawal event is delivered over (RabbitMQ in the default setup). Only the outbox
/// dispatcher calls this, after the event row has been committed with the balance change. A
/// failure throws, and the dispatcher leaves the row pending and tries again later.
/// </summary>
public interface IWithdrawalEventBus
{
    Task PublishAsync(WithdrawalCompleted withdrawalEvent, CancellationToken cancellationToken = default);
}
