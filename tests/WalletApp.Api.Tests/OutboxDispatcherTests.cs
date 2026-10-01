using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WalletApp.Application.Abstractions;
using WalletApp.Domain;
using WalletApp.Infrastructure.Events;
using WalletApp.Infrastructure.Persistence;
using Xunit;

namespace WalletApp.Api.Tests;

public class OutboxDispatcherTests : IClassFixture<WalletApiFactory>
{
    private readonly WalletApiFactory _factory;
    private readonly HttpClient _client;

    public OutboxDispatcherTests(WalletApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private OutboxDispatcher CreateDispatcher(IWithdrawalEventBus bus, int batchSize = 50) =>
        new(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            bus,
            Options.Create(new OutboxOptions { BatchSize = batchSize }),
            NullLogger<OutboxDispatcher>.Instance);

    private async Task<Guid> WithdrawAsync(int walletId, decimal amount)
    {
        var response = await _client.PostAsJsonAsync($"/api/wallets/{walletId}/withdrawals", new { amount });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WalletApp.Application.Wallets.WithdrawResult>();
        return result!.WithdrawalId;
    }

    private async Task<WithdrawalEventRecord> GetRecordAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.WithdrawalEvents.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    [Fact]
    public async Task Withdrawal_LeavesOutboxRowPendingUntilDispatched()
    {
        var wallet = await _factory.SeedWalletAsync(100m);
        var id = await WithdrawAsync(wallet.Id, 10m);

        var record = await GetRecordAsync(id);

        Assert.Null(record.DispatchedAtUtc);
    }

    [Fact]
    public async Task DispatchPending_PublishesEventAndStampsRow()
    {
        var wallet = await _factory.SeedWalletAsync(100m);
        var id = await WithdrawAsync(wallet.Id, 25m);
        var bus = new RecordingBus();

        await CreateDispatcher(bus).DispatchPendingAsync();

        var published = Assert.Single(bus.Published, e => e.EventId == id);
        Assert.Equal(wallet.Id, published.WalletId);
        Assert.Equal(25m, published.Amount);
        Assert.Equal(75m, published.BalanceAfter);
        Assert.NotNull((await GetRecordAsync(id)).DispatchedAtUtc);
    }

    [Fact]
    public async Task DispatchPending_DoesNotRedeliverAlreadyDispatchedEvents()
    {
        var wallet = await _factory.SeedWalletAsync(100m);
        var id = await WithdrawAsync(wallet.Id, 5m);
        var bus = new RecordingBus();
        var dispatcher = CreateDispatcher(bus);

        await dispatcher.DispatchPendingAsync();
        await dispatcher.DispatchPendingAsync();

        Assert.Single(bus.Published, e => e.EventId == id);
    }

    [Fact]
    public async Task DispatchPending_WhenBrokerIsDown_KeepsRowPendingAndDeliversOnRetry()
    {
        var wallet = await _factory.SeedWalletAsync(100m);
        var id = await WithdrawAsync(wallet.Id, 5m);
        var bus = new RecordingBus { Fail = true };
        var dispatcher = CreateDispatcher(bus);

        var failedPass = await dispatcher.DispatchPendingAsync();

        Assert.Equal(0, failedPass);
        Assert.Null((await GetRecordAsync(id)).DispatchedAtUtc);

        bus.Fail = false;
        await dispatcher.DispatchPendingAsync();

        Assert.Single(bus.Published, e => e.EventId == id);
        Assert.NotNull((await GetRecordAsync(id)).DispatchedAtUtc);
    }

    [Fact]
    public async Task DispatchPending_DeliversInTheOrderWithdrawalsHappened()
    {
        var wallet = await _factory.SeedWalletAsync(100m);
        var first = await WithdrawAsync(wallet.Id, 1m);
        var second = await WithdrawAsync(wallet.Id, 2m);
        var third = await WithdrawAsync(wallet.Id, 3m);
        var bus = new RecordingBus();

        await CreateDispatcher(bus).DispatchPendingAsync();

        var ours = bus.Published.Where(e => e.WalletId == wallet.Id).Select(e => e.EventId).ToList();
        Assert.Equal(new[] { first, second, third }, ours);
    }

    private sealed class RecordingBus : IWithdrawalEventBus
    {
        public List<WithdrawalCompleted> Published { get; } = [];
        public bool Fail { get; set; }

        public Task PublishAsync(WithdrawalCompleted withdrawalEvent, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("broker unavailable");
            }

            Published.Add(withdrawalEvent);
            return Task.CompletedTask;
        }
    }
}
