namespace WalletApp.Application.Wallets;

/// <summary>One completed withdrawal as shown in a wallet's history.</summary>
public sealed record WithdrawalSummary(
    Guid WithdrawalId,
    decimal Amount,
    decimal BalanceAfter,
    string Currency,
    DateTimeOffset OccurredAtUtc);
