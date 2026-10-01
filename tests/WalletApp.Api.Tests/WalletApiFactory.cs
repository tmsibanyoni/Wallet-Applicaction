using System.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WalletApp.Domain;
using WalletApp.Infrastructure.Persistence;
using Xunit;

namespace WalletApp.Api.Tests;

/// <summary>
/// Boots the real API host (same Program.cs, same migrations/seed logic) against a uniquely
/// named database on the local SQL Server instance, so integration tests exercise real
/// transactions and real optimistic-concurrency behaviour instead of a fake provider. The
/// database is dropped once the test class finishes.
/// </summary>
public class WalletApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string MasterConnectionString = "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;";

    public string DatabaseName { get; } = $"WalletApp_Test_{Guid.NewGuid():N}";

    private string TestConnectionString =>
        $"Server=localhost;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True;";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:WalletDb"] = TestConnectionString,
                // No broker in the test host; tests drive the outbox dispatcher themselves.
                ["EventBus:Provider"] = "Log",
                ["Outbox:Enabled"] = "false",
                // The load tests send bursts that a real client limit would (rightly) reject.
                ["RateLimiting:Enabled"] = "false",
            };

            foreach (var (key, value) in ExtraSettings)
            {
                settings[key] = value;
            }

            config.AddInMemoryCollection(settings);
        });
    }

    /// <summary>Settings a derived factory adds on top of (or instead of) the defaults above.</summary>
    protected virtual IReadOnlyDictionary<string, string?> ExtraSettings { get; } = new Dictionary<string, string?>();

    /// <summary>The id of the wallet the app seeds on startup, as configured in appsettings.json.</summary>
    public int SeedWalletId => Services.GetRequiredService<IOptions<WalletSeedOptions>>().Value.WalletId;

    // Wallet.Id is assigned by the caller (no IDENTITY column - see WalletDbContext), so tests that
    // seed their own wallets need their own unique ids. Starts well clear of the real seed id (1).
    private static int _nextTestWalletId = 10_000;

    public async Task<Wallet> SeedWalletAsync(decimal balance, string currency = "USD")
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        var id = Interlocked.Increment(ref _nextTestWalletId);
        var wallet = new Wallet(id, $"Test Wallet {id}", balance, currency);
        dbContext.Wallets.Add(wallet);
        await dbContext.SaveChangesAsync();

        return wallet;
    }

    public async Task<decimal> GetPersistedBalanceAsync(int walletId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        // AsNoTracking + a fresh scope avoids the first-level cache masking a stale read.
        var wallet = await dbContext.Wallets.FindAsync(walletId);
        return wallet!.Balance;
    }

    public Task InitializeAsync()
    {
        // Touching the server forces Program.cs's startup block (migrate + seed) to run
        // against TestConnectionString before any test issues a request.
        using var _ = CreateClient();
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Tear down the host/connection pool first so nothing still holds the test database open.
        await base.DisposeAsync();

        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}];";
        await command.ExecuteNonQueryAsync();
    }
}
