namespace WalletApp.Domain.Exceptions;

/// <summary>
/// Thrown by an <see cref="Abstractions.IWalletRepository"/> implementation when a save fails
/// because another request modified the same wallet first (optimistic concurrency).
/// Kept persistence-technology-agnostic so the Application layer never depends on EF Core types.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public int WalletId { get; }

    public ConcurrencyConflictException(int walletId, Exception innerException)
        : base($"Wallet '{walletId}' was modified concurrently by another request.", innerException)
    {
        WalletId = walletId;
    }
}
