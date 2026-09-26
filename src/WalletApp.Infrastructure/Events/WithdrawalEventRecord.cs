using WalletApp.Domain;

namespace WalletApp.Infrastructure.Events;

/// <summary>
/// Durable, append-only record of a withdrawal event. Written in the same database transaction
/// as the wallet balance update (see <see cref="Repositories.EfWalletRepository.SaveWithdrawalAsync"/>),
/// which is what makes "emit an event on success" a guarantee rather than a best-effort side effect:
/// this row exists if and only if the balance update was committed. A production system would add a
/// background dispatcher that relays undelivered rows here to a real broker and marks them dispatched
/// (the classic transactional outbox pattern) instead of relying solely on the in-process bus in
/// <see cref="InProcessWithdrawalEventBus"/>.
/// </summary>
public sealed class WithdrawalEventRecord
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public string EventType { get; private set; } = nameof(WithdrawalCompleted);
    public decimal Amount { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }

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
}
