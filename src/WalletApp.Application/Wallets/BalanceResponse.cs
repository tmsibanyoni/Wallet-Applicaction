namespace WalletApp.Application.Wallets;

public sealed record BalanceResponse(int WalletId, decimal Balance, string Currency);
