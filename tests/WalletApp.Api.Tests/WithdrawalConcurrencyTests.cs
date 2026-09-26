using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace WalletApp.Api.Tests;

/// <summary>
/// Proves the "never negative balance" invariant holds under real concurrent load against a
/// real database, not just in a single-threaded unit test.
/// </summary>
public class WithdrawalConcurrencyTests : IClassFixture<WalletApiFactory>
{
    private readonly WalletApiFactory _factory;

    public WithdrawalConcurrencyTests(WalletApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrentWithdrawals_NeverOverdrawTheWallet()
    {
        const decimal startingBalance = 500m;
        const decimal amountPerWithdrawal = 100m;
        const int concurrentRequests = 20; // 20 * 100 = 2000 requested against 500 available

        var wallet = await _factory.SeedWalletAsync(startingBalance);

        var client = _factory.CreateClient();
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => client.PostAsJsonAsync($"/api/wallets/{wallet.Id}/withdrawals", new { amount = amountPerWithdrawal }));

        var responses = await Task.WhenAll(tasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var insufficientFundsCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Unexpected status {r.StatusCode}"));

        // Exactly enough withdrawals succeeded to exhaust the balance, no more.
        Assert.Equal((int)(startingBalance / amountPerWithdrawal), successCount);
        Assert.Equal(concurrentRequests - successCount, insufficientFundsCount);

        var finalBalance = await _factory.GetPersistedBalanceAsync(wallet.Id);
        Assert.Equal(0m, finalBalance);
        Assert.True(finalBalance >= 0m, "Balance must never go negative.");
    }
}
