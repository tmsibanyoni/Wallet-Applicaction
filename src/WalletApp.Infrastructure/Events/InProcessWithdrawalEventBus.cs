using System.Threading.Channels;
using WalletApp.Application.Abstractions;
using WalletApp.Domain;

namespace WalletApp.Infrastructure.Events;

/// <summary>
/// Minimal pub/sub implementation built on <see cref="Channel{T}"/> so the app has zero external
/// event-broker dependency for local development. It plugs into the same
/// <see cref="IWithdrawalEventBus"/> port a real broker publisher (RabbitMQ, Kafka, Azure Service
/// Bus, SNS, ...) would implement, so swapping the transport later does not touch
/// <see cref="Application.Wallets.WalletService"/>.
/// </summary>
public sealed class InProcessWithdrawalEventBus : IWithdrawalEventBus
{
    private readonly Channel<WithdrawalCompleted> _channel = Channel.CreateUnbounded<WithdrawalCompleted>();

    public ChannelReader<WithdrawalCompleted> Reader => _channel.Reader;

    public async Task PublishAsync(WithdrawalCompleted withdrawalEvent, CancellationToken cancellationToken = default)
        => await _channel.Writer.WriteAsync(withdrawalEvent, cancellationToken);
}
