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
            .AddJsonFile(FindApiAppSettingsPath(), optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("WalletDb")
            ?? throw new InvalidOperationException(
                "Could not resolve the 'WalletDb' connection string for design-time migrations. " +
                "Could not locate src/WalletApp.Api/appsettings.json from either the current directory " +
                "or this assembly's location, and no ConnectionStrings__WalletDb environment variable is set.");

        var optionsBuilder = new DbContextOptionsBuilder<WalletDbContext>();
        optionsBuilder.UseSqlServer(
            connectionString,
            sql => sql.MigrationsAssembly(typeof(WalletDbContextFactory).Assembly.FullName));

        return new WalletDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// `dotnet ef` doesn't guarantee its process's current directory is the repository root, so
    /// this looks there first (the common case) and falls back to walking up from this compiled
    /// assembly's own location (stable regardless of where the CLI was invoked from).
    /// </summary>
    private static string FindApiAppSettingsPath()
    {
        var relative = Path.Combine("src", "WalletApp.Api", "appsettings.json");
        var fromCwd = Path.Combine(Directory.GetCurrentDirectory(), relative);
        if (File.Exists(fromCwd))
        {
            return fromCwd;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return relative;
    }
}
