namespace WalletApp.Application.Wallets;

public sealed record BalanceResponse(Guid WalletId, decimal Balance, string Currency);
