using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WalletApp.Infrastructure.Events;

/// <summary>
/// Stand-in "subscriber" that demonstrates a consumer reacting to withdrawal events
/// out-of-band from the HTTP request. Represents where a real system would notify other
/// bounded contexts (e.g. fraud monitoring, statements, notifications) by subscribing to
/// the same event on a real broker.
/// </summary>
public sealed class WithdrawalEventLoggingConsumer : BackgroundService
{
    private readonly InProcessWithdrawalEventBus _eventBus;
    private readonly ILogger<WithdrawalEventLoggingConsumer> _logger;

    public WithdrawalEventLoggingConsumer(InProcessWithdrawalEventBus eventBus, ILogger<WithdrawalEventLoggingConsumer> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var withdrawalEvent in _eventBus.Reader.ReadAllAsync(stoppingToken))
        {
            _logger.LogInformation(
                "Withdrawal event consumed: WalletId={WalletId} Amount={Amount} {Currency} BalanceAfter={BalanceAfter} OccurredAtUtc={OccurredAtUtc}",
                withdrawalEvent.WalletId,
                withdrawalEvent.Amount,
                withdrawalEvent.Currency,
                withdrawalEvent.BalanceAfter,
                withdrawalEvent.OccurredAtUtc);
        }
    }
}
