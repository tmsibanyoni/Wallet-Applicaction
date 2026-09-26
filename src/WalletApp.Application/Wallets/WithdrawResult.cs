namespace WalletApp.Application.Wallets;

public sealed record WithdrawResult(
    Guid WithdrawalId,
    int WalletId,
    decimal Amount,
    decimal BalanceAfter,
    string Currency,
    DateTimeOffset OccurredAtUtc);
