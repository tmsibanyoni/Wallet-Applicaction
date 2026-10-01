using WalletApp.Domain.Exceptions;

namespace WalletApp.Domain.Abstractions;

/// <summary>
/// Persistence port for the <see cref="Wallet"/> aggregate. Deliberately narrow (no generic CRUD)
/// since withdrawal is the only mutation in scope for this exercise.
/// </summary>
public interface IWalletRepository
{
    Task<Wallet?> GetByIdAsync(int walletId, CancellationToken cancellationToken = default);

    /// <summary>Finds the withdrawal previously completed under <paramref name="idempotencyKey"/> for this wallet, if any.</summary>
    Task<WithdrawalCompleted?> FindWithdrawalByKeyAsync(int walletId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a wallet that has just had a withdrawal applied to it, together with the
    /// corresponding event record, as a single atomic unit (see WalletApp.Infrastructure for the
    /// transactional-outbox-style implementation).
    /// </summary>
    /// <exception cref="ConcurrencyConflictException">
    /// Thrown when the wallet was modified by another request since it was loaded.
    /// </exception>
    /// <exception cref="DuplicateIdempotencyKeyException">
    /// Thrown when the event carries an idempotency key that another request already stored.
    /// </exception>
    Task SaveWithdrawalAsync(Wallet wallet, WithdrawalCompleted withdrawalEvent, CancellationToken cancellationToken = default);
}
