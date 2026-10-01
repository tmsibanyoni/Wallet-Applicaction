namespace WalletApp.Domain.Exceptions;

/// <summary>Raised by the repository when another request stored the same idempotency key first.</summary>
public sealed class DuplicateIdempotencyKeyException : Exception
{
    public DuplicateIdempotencyKeyException(int walletId, string idempotencyKey, Exception innerException)
        : base($"A withdrawal for wallet {walletId} with idempotency key '{idempotencyKey}' already exists.", innerException)
    {
    }
}
