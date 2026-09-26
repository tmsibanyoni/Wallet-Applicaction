namespace WalletApp.Domain.Exceptions;

/// <summary>Thrown when a requested wallet does not exist.</summary>
public sealed class WalletNotFoundException : Exception
{
    public Guid WalletId { get; }

    public WalletNotFoundException(Guid walletId)
        : base($"Wallet '{walletId}' was not found.")
    {
        WalletId = walletId;
    }
}
