using Microsoft.EntityFrameworkCore;
using WalletApp.Domain;

namespace WalletApp.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations (creating the database and tables on first run) and
/// guarantees the configured seed wallet exists. Called once at application startup - see
/// WalletApp.Api Program.cs.
/// </summary>
public static class WalletDbInitializer
{
    public static async Task InitializeAsync(WalletDbContext dbContext, WalletSeedOptions seed, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        var seedExists = await dbContext.Wallets.AnyAsync(w => w.Id == seed.WalletId, cancellationToken);
        if (!seedExists)
        {
            dbContext.Wallets.Add(new Wallet(seed.WalletId, seed.OwnerName, seed.InitialBalance, seed.Currency));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
