using Microsoft.Extensions.Diagnostics.HealthChecks;
using WalletApp.Infrastructure.Persistence;

namespace WalletApp.Api.Infrastructure;

/// <summary>Reports unhealthy when the wallet database cannot be reached.</summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly WalletDbContext _dbContext;

    public DatabaseHealthCheck(WalletDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The wallet database cannot be reached.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The wallet database cannot be reached.", ex);
        }
    }
}
