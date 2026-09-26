using System.Net;
using System.Net.Http.Json;
using WalletApp.Application.Wallets;
using Xunit;

namespace WalletApp.Api.Tests;

public class WalletsControllerTests : IClassFixture<WalletApiFactory>
{
    private readonly WalletApiFactory _factory;
    private readonly HttpClient _client;

    public WalletsControllerTests(WalletApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetBalance_SeedWallet_ReturnsKnownPositiveBalance()
    {
        var response = await _client.GetAsync($"/api/wallets/{_factory.SeedWalletId}/balance");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BalanceResponse>();

        Assert.NotNull(body);
        Assert.Equal(_factory.SeedWalletId, body!.WalletId);
        Assert.True(body.Balance > 0);
    }

    [Fact]
    public async Task GetBalance_UnknownWallet_Returns404()
    {
        var response = await _client.GetAsync($"/api/wallets/{Guid.NewGuid()}/balance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Withdraw_WithSufficientFunds_ReducesBalanceAndReturns200()
    {
        var wallet = await _factory.SeedWalletAsync(balance: 200m);

        var response = await _client.PostAsJsonAsync($"/api/wallets/{wallet.Id}/withdrawals", new { amount = 75m });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WithdrawResult>();
        Assert.NotNull(result);
        Assert.Equal(125m, result!.BalanceAfter);

        var balanceResponse = await _client.GetFromJsonAsync<BalanceResponse>($"/api/wallets/{wallet.Id}/balance");
        Assert.Equal(125m, balanceResponse!.Balance);
    }

    [Fact]
    public async Task Withdraw_MoreThanBalance_Returns409AndLeavesBalanceUnchanged()
    {
        var wallet = await _factory.SeedWalletAsync(balance: 50m);

        var response = await _client.PostAsJsonAsync($"/api/wallets/{wallet.Id}/withdrawals", new { amount = 50.01m });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var balance = await _factory.GetPersistedBalanceAsync(wallet.Id);
        Assert.Equal(50m, balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Withdraw_WithNonPositiveAmount_Returns400(decimal amount)
    {
        var wallet = await _factory.SeedWalletAsync(balance: 50m);

        var response = await _client.PostAsJsonAsync($"/api/wallets/{wallet.Id}/withdrawals", new { amount });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Withdraw_UnknownWallet_Returns404()
    {
        var response = await _client.PostAsJsonAsync($"/api/wallets/{Guid.NewGuid()}/withdrawals", new { amount = 10m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
