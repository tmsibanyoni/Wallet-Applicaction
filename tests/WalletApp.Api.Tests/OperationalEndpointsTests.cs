using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WalletApp.Api.Infrastructure;
using WalletApp.Infrastructure.Persistence;
using Xunit;

namespace WalletApp.Api.Tests;

/// <summary>Turns the limiter on with tiny limits so the tests can hit them.</summary>
public sealed class RateLimitedApiFactory : WalletApiFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraSettings { get; } = new Dictionary<string, string?>
    {
        ["RateLimiting:Enabled"] = "true",
        ["RateLimiting:WritePermitLimit"] = "2",
        ["RateLimiting:ReadPermitLimit"] = "50",
        ["RateLimiting:WindowSeconds"] = "60",
    };
}

public class OperationalEndpointsTests : IClassFixture<WalletApiFactory>, IClassFixture<RateLimitedApiFactory>
{
    private readonly WalletApiFactory _factory;
    private readonly RateLimitedApiFactory _limitedFactory;

    public OperationalEndpointsTests(WalletApiFactory factory, RateLimitedApiFactory limitedFactory)
    {
        _factory = factory;
        _limitedFactory = limitedFactory;
    }

    [Fact]
    public async Task Liveness_ReturnsHealthy()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_ReturnsHealthyWhenTheDatabaseIsReachable()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DatabaseHealthCheck_ReportsUnhealthyWhenTheServerCannotBeReached()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer("Server=localhost,1;Database=Nope;Connect Timeout=2;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;
        await using var dbContext = new WalletDbContext(options);

        var result = await new DatabaseHealthCheck(dbContext).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    // One test on purpose: both halves share the same client's limiter window on this host.
    [Fact]
    public async Task RateLimiting_RejectsWritesOverTheLimitButStillServesReadsAndHealthChecks()
    {
        var wallet = await _limitedFactory.SeedWalletAsync(balance: 100m);
        var client = _limitedFactory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 4; i++)
        {
            var response = await client.PostAsJsonAsync($"/api/wallets/{wallet.Id}/withdrawals", new { amount = 1m });
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected ??= response;
            }
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
        Assert.NotNull(rejected);
        Assert.True(rejected!.Headers.Contains("Retry-After"));
        Assert.Equal(98m, await _limitedFactory.GetPersistedBalanceAsync(wallet.Id));

        var read = await client.GetAsync($"/api/wallets/{wallet.Id}/balance");
        var health = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
