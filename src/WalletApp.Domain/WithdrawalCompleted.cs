namespace WalletApp.Domain;

/// <summary>
/// Domain event describing a withdrawal that has already been applied to a wallet.
/// This is the payload shape published on the withdrawal event mechanism (see
/// WalletApp.Infrastructure.Events) and persisted in the event log.
/// </summary>
public sealed record WithdrawalCompleted(
    Guid EventId,
    int WalletId,
    decimal Amount,
    decimal BalanceAfter,
    string Currency,
    DateTimeOffset OccurredAtUtc);
