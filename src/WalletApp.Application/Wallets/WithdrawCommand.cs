namespace WalletApp.Application.Wallets;

/// <summary>
/// A request to withdraw funds. <paramref name="Currency"/> is optional; when supplied it must
/// match the wallet's own currency, so a client can never withdraw "10" and have it silently
/// interpreted in a currency it didn't intend.
/// </summary>
public sealed record WithdrawCommand(int WalletId, decimal Amount, string? Currency = null);
