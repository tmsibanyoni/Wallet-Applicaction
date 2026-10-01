using System.Net;
using System.Net.Http.Json;
using WalletApp.Application.Wallets;
using Xunit;

namespace WalletApp.Api.Tests;

public class IdempotencyTests : IClassFixture<WalletApiFactory>
{
    private readonly WalletApiFactory _factory;
    private readonly HttpClient _client;

    public IdempotencyTests(WalletApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<HttpResponseMessage> PostWithdrawalAsync(int walletId, decimal amount, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/wallets/{walletId}/withdrawals")
        {
            Content = JsonContent.Create(new { amount }),
        };

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return _client.SendAsync(request);
    }

    [Fact]
    public async Task Withdraw_SameKeyTwice_DeductsOnceAndReplaysTheFirstResult()
    {
        var wallet = await _factory.SeedWalletAsync(balance: 200m);
        var key = Guid.NewGuid().ToString();

        var first = await PostWithdrawalAsync(wallet.Id, 50m, key);
        var second = await PostWithdrawalAsync(wallet.Id, 50m, key);

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.True(second.Headers.Contains("Idempotent-Replayed"));

        var firstBody = await first.Content.ReadFromJsonAsync<WithdrawResult>();
        var secondBody = await second.Content.ReadFromJsonAsync<WithdrawResult>();
        Assert.Equal(firstBody!.WithdrawalId, secondBody!.WithdrawalId);
        Assert.Equal(150m, secondBody.BalanceAfter);
        Assert.Equal(150m, await _factory.GetPersistedBalanceAsync(wallet.Id));
    }

    [Fact]
    public async Task Withdraw_SameKeyWithDifferentAmount_Returns422AndDoesNotWithdraw()
    {
        var wallet = await _factory.SeedWalletAsync(balance: 200m);
        var key = Guid.NewGuid().ToString();

        (await PostWithdrawalAsync(wallet.Id, 50m, key)).EnsureSuccessStatusCode();
        var second = await PostWithdrawalAsync(wallet.Id, 60m, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal(150m, await _factory.GetPersistedBalanceAsync(wallet.Id));
    }

    [Fact]
    public async Task Withdraw_WithoutKey_TwoIdenticalRequestsBothWithdraw()
    {
        var wallet = await _factory.SeedWalletAsync(balance: 200m);

        (await PostWithdrawalAsync(wallet.Id, 50m, null)).EnsureSuccessStatusCode();
        (await PostWithdrawalAsync(wallet.Id, 50m, null)).EnsureSuccessStatusCode();

        Assert.Equal(100m, await _factory.GetPersistedBalanceAsync(wallet.Id));
    }

    [Fact]
    public async Task Withdraw_SameKeyOnDifferentWallets_IsIndependent()
    {
        var first = await _factory.SeedWalletAsync(balance: 100m);
        var second = await _factory.SeedWalletAsync(balance: 100m);
        var key = Guid.NewGuid().ToString();

        (await PostWithdrawalAsync(first.Id, 10m, key)).EnsureSuccessStatusCode();
        (await PostWithdrawalAsync(second.Id, 10m, key)).EnsureSuccessStatusCode();

        Assert.Equal(90m, await _factory.GetPersistedBalanceAsync(first.Id));
        Assert.Equal(90m, await _factory.GetPersistedBalanceAsync(second.Id));
    }

    [Fact]
    public async Task Withdraw_KeyTooLong_Returns400()
    {
        var wallet = await _factory.SeedWalletAsync(balance: 100m);

        var response = await PostWithdrawalAsync(wallet.Id, 10m, new string('k', 101));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(100m, await _factory.GetPersistedBalanceAsync(wallet.Id));
    }

    [Fact]
    public async Task Withdraw_ParallelRequestsWithSameKey_DeductOnceAndAllSucceed()
    {
        var wallet = await _factory.SeedWalletAsync(balance: 500m);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => PostWithdrawalAsync(wallet.Id, 100m, key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var ids = new HashSet<Guid>();
        foreach (var response in responses)
        {
            ids.Add((await response.Content.ReadFromJsonAsync<WithdrawResult>())!.WithdrawalId);
        }

        Assert.Single(ids);
        Assert.Equal(400m, await _factory.GetPersistedBalanceAsync(wallet.Id));
    }
}
