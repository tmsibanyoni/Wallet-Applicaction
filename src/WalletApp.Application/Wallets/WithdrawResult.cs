namespace WalletApp.Application.Wallets;

public sealed record WithdrawResult(
    Guid WithdrawalId,
    Guid WalletId,
    decimal Amount,
    decimal BalanceAfter,
    string Currency,
    DateTimeOffset OccurredAtUtc);
