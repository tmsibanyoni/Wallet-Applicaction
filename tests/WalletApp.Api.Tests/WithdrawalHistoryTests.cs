using System.Net;
using System.Net.Http.Json;
using WalletApp.Application.Wallets;
using Xunit;

namespace WalletApp.Api.Tests;

public class WithdrawalHistoryTests : IClassFixture<WalletApiFactory>
{
    private readonly WalletApiFactory _factory;
    private readonly HttpClient _client;

    public WithdrawalHistoryTests(WalletApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetWithdrawals_ReturnsTheWalletsWithdrawalsNewestFirst()
    {
        var wallet = await _factory.SeedWalletAsync(balance: 100m);
        var other = await _factory.SeedWalletAsync(balance: 100m);

        (await _client.PostAsJsonAsync($"/api/wallets/{wallet.Id}/withdrawals", new { amount = 10m })).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/wallets/{wallet.Id}/withdrawals", new { amount = 20m })).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/wallets/{other.Id}/withdrawals", new { amount = 5m })).EnsureSuccessStatusCode();

        var history = await _client.GetFromJsonAsync<List<WithdrawalSummary>>($"/api/wallets/{wallet.Id}/withdrawals");

        Assert.NotNull(history);
        Assert.Equal([20m, 10m], history!.Select(h => h.Amount));
        Assert.Equal([70m, 90m], history.Select(h => h.BalanceAfter));
    }

    [Fact]
    public async Task GetWithdrawals_HonoursTheLimit()
    {
        var wallet = await _factory.SeedWalletAsync(balance: 100m);
        for (var i = 0; i < 3; i++)
        {
            (await _client.PostAsJsonAsync($"/api/wallets/{wallet.Id}/withdrawals", new { amount = 1m })).EnsureSuccessStatusCode();
        }

        var history = await _client.GetFromJsonAsync<List<WithdrawalSummary>>($"/api/wallets/{wallet.Id}/withdrawals?limit=2");

        Assert.Equal(2, history!.Count);
    }

    [Fact]
    public async Task GetWithdrawals_WalletWithNoWithdrawals_ReturnsAnEmptyList()
    {
        var wallet = await _factory.SeedWalletAsync(balance: 100m);

        var history = await _client.GetFromJsonAsync<List<WithdrawalSummary>>($"/api/wallets/{wallet.Id}/withdrawals");

        Assert.Empty(history!);
    }

    [Fact]
    public async Task GetWithdrawals_UnknownWallet_Returns404()
    {
        var response = await _client.GetAsync("/api/wallets/999999999/withdrawals");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task GetWithdrawals_LimitOutOfRange_Returns400(int limit)
    {
        var wallet = await _factory.SeedWalletAsync(balance: 100m);

        var response = await _client.GetAsync($"/api/wallets/{wallet.Id}/withdrawals?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
