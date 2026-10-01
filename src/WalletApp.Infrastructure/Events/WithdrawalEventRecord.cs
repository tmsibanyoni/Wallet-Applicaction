using WalletApp.Domain;

namespace WalletApp.Infrastructure.Events;

/// <summary>
/// Outbox row for a withdrawal event. Written in the same database transaction as the wallet
/// balance update (see <see cref="Repositories.EfWalletRepository.SaveWithdrawalAsync"/>), so the
/// row exists if and only if the balance change was committed. <see cref="OutboxDispatcher"/> later
/// relays rows whose <see cref="DispatchedAtUtc"/> is still null to the event bus and stamps them.
/// Delivery is at-least-once: consumers de-duplicate on the event id.
/// </summary>
public sealed class WithdrawalEventRecord
{
    public Guid Id { get; private set; }
    public int WalletId { get; private set; }
    public string EventType { get; private set; } = nameof(WithdrawalCompleted);
    public decimal Amount { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset? DispatchedAtUtc { get; private set; }

    private WithdrawalEventRecord()
    {
    }

    public static WithdrawalEventRecord From(WithdrawalCompleted withdrawalEvent) => new()
    {
        Id = withdrawalEvent.EventId,
        WalletId = withdrawalEvent.WalletId,
        Amount = withdrawalEvent.Amount,
        BalanceAfter = withdrawalEvent.BalanceAfter,
        Currency = withdrawalEvent.Currency,
        OccurredAtUtc = withdrawalEvent.OccurredAtUtc,
    };

    public WithdrawalCompleted ToEvent() => new(Id, WalletId, Amount, BalanceAfter, Currency, OccurredAtUtc);

    public void MarkDispatched(DateTimeOffset dispatchedAtUtc) => DispatchedAtUtc = dispatchedAtUtc;
}
