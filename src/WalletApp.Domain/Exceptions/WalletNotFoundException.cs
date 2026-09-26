namespace WalletApp.Domain.Exceptions;

/// <summary>Thrown when a requested wallet does not exist.</summary>
public sealed class WalletNotFoundException : Exception
{
    public int WalletId { get; }

    public WalletNotFoundException(int walletId)
        : base($"Wallet '{walletId}' was not found.")
    {
        WalletId = walletId;
    }
}
