using Microsoft.Extensions.Logging;
using WalletApp.Application.Abstractions;
using WalletApp.Domain;

namespace WalletApp.Infrastructure.Events;

/// <summary>Broker-less transport that just logs each event; used when no RabbitMQ is available.</summary>
public sealed class LoggingWithdrawalEventBus : IWithdrawalEventBus
{
    private readonly ILogger<LoggingWithdrawalEventBus> _logger;

    public LoggingWithdrawalEventBus(ILogger<LoggingWithdrawalEventBus> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(WithdrawalCompleted withdrawalEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Withdrawal event {EventId}: WalletId={WalletId} Amount={Amount} {Currency} BalanceAfter={BalanceAfter}",
            withdrawalEvent.EventId, withdrawalEvent.WalletId, withdrawalEvent.Amount,
            withdrawalEvent.Currency, withdrawalEvent.BalanceAfter);
        return Task.CompletedTask;
    }
}
