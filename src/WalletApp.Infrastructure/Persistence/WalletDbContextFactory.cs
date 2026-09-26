using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace WalletApp.Infrastructure.Persistence;

/// <summary>
/// Lets the `dotnet ef migrations` CLI construct a <see cref="WalletDbContext"/> at design time
/// without spinning up the full ASP.NET Core host. Not used at application runtime - the app
/// itself gets its connection string from configuration via DI (see WalletApp.Api Program.cs).
/// Reads the same appsettings.json the app uses (run `dotnet ef` from the repository root, or set
/// the ConnectionStrings__WalletDb environment variable) so the connection string is defined in
/// exactly one place.
/// </summary>
public sealed class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine("src", "WalletApp.Api", "appsettings.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("WalletDb")
            ?? throw new InvalidOperationException(
                "Could not resolve the 'WalletDb' connection string for design-time migrations. " +
                "Run 'dotnet ef' from the repository root (so src/WalletApp.Api/appsettings.json is reachable), " +
                "or set the ConnectionStrings__WalletDb environment variable.");

        var optionsBuilder = new DbContextOptionsBuilder<WalletDbContext>();
        optionsBuilder.UseSqlServer(
            connectionString,
            sql => sql.MigrationsAssembly(typeof(WalletDbContextFactory).Assembly.FullName));

        return new WalletDbContext(optionsBuilder.Options);
    }
}
