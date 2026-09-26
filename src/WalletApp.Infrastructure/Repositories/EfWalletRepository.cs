using Microsoft.EntityFrameworkCore;
using WalletApp.Domain;
using WalletApp.Domain.Abstractions;
using WalletApp.Domain.Exceptions;
using WalletApp.Infrastructure.Events;
using WalletApp.Infrastructure.Persistence;

namespace WalletApp.Infrastructure.Repositories;

public sealed class EfWalletRepository : IWalletRepository
{
    private readonly WalletDbContext _dbContext;

    public EfWalletRepository(WalletDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Wallet?> GetByIdAsync(int walletId, CancellationToken cancellationToken = default)
        => _dbContext.Wallets.FirstOrDefaultAsync(w => w.Id == walletId, cancellationToken);

    public async Task SaveWithdrawalAsync(Wallet wallet, WithdrawalCompleted withdrawalEvent, CancellationToken cancellationToken = default)
    {
        // 'wallet' is already tracked by this scoped DbContext (it was loaded via GetByIdAsync),
        // so its mutated Balance/Version are picked up automatically; adding the event record here
        // means both writes commit in the same SaveChanges transaction.
        var eventRecord = WithdrawalEventRecord.From(withdrawalEvent);
        _dbContext.WithdrawalEvents.Add(eventRecord);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // The DbContext is scoped to the whole request, so WalletService's retry loop will
            // call GetByIdAsync again on this *same* context. EF's identity map would otherwise
            // hand back this same tracked (and now stale-mutated) Wallet instance instead of a
            // fresh read, so the retry would never converge. Reloading resets it to the
            // currently persisted row; detaching the failed event insert stops it from being
            // resurrected alongside the retry's own (differently-valued) event record.
            _dbContext.Entry(eventRecord).State = EntityState.Detached;

            foreach (var entry in ex.Entries)
            {
                await entry.ReloadAsync(cancellationToken);
            }

            throw new ConcurrencyConflictException(wallet.Id, ex);
        }
    }
}
