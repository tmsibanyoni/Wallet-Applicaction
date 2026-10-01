using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using WalletApp.Domain;

namespace WalletApp.Infrastructure.Events;

/// <summary>
/// Example subscriber that reads withdrawal events off the queue and logs them, showing how another
/// service (fraud checks, statements, notifications) would consume the same stream. It reconnects if
/// the broker is unavailable and acknowledges each message only after handling it.
/// </summary>
public sealed class RabbitMqWithdrawalEventConsumer : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitMqOptions _options;
    private readonly EventBusOptions _busOptions;
    private readonly ILogger<RabbitMqWithdrawalEventConsumer> _logger;

    public RabbitMqWithdrawalEventConsumer(
        IOptions<RabbitMqOptions> options,
        IOptions<EventBusOptions> busOptions,
        ILogger<RabbitMqWithdrawalEventConsumer> logger)
    {
        _options = options.Value;
        _busOptions = busOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!string.Equals(_busOptions.Provider, EventBusOptions.RabbitMqProvider, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Withdrawal event consumer lost the broker; retrying in {Delay}.", ReconnectDelay);
            }

            try
            {
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await using var connection = await RabbitMqTopology.CreateFactory(_options).CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await RabbitMqTopology.DeclareAsync(channel, _options, stoppingToken);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var withdrawalEvent = JsonSerializer.Deserialize<WithdrawalCompleted>(delivery.Body.Span);

            if (withdrawalEvent is not null)
            {
                _logger.LogInformation(
                    "Withdrawal event consumed: EventId={EventId} WalletId={WalletId} Amount={Amount} {Currency} BalanceAfter={BalanceAfter}",
                    withdrawalEvent.EventId, withdrawalEvent.WalletId, withdrawalEvent.Amount,
                    withdrawalEvent.Currency, withdrawalEvent.BalanceAfter);
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        };

        await channel.BasicConsumeAsync(_options.Queue, autoAck: false, consumer, stoppingToken);

        using var registration = stoppingToken.Register(() => closed.TrySetCanceled(stoppingToken));
        await closed.Task;
    }
}
