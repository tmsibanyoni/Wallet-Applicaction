using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using WalletApp.Application.Abstractions;
using WalletApp.Domain;

namespace WalletApp.Infrastructure.Events;

/// <summary>
/// Publishes withdrawal events to a durable topic exchange on RabbitMQ, using publisher confirms so
/// <see cref="PublishAsync"/> only returns once the broker has accepted the message. A durable queue
/// is declared and bound as well, so events are kept even when no consumer is running yet.
/// </summary>
public sealed class RabbitMqWithdrawalEventBus : IWithdrawalEventBus, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqWithdrawalEventBus(IOptions<RabbitMqOptions> options)
    {
        _options = options.Value;
    }

    public async Task PublishAsync(WithdrawalCompleted withdrawalEvent, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await EnsureChannelAsync(cancellationToken);

            var properties = new BasicProperties
            {
                MessageId = withdrawalEvent.EventId.ToString(),
                Type = nameof(WithdrawalCompleted),
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(withdrawalEvent.OccurredAtUtc.ToUnixTimeSeconds()),
            };

            var body = JsonSerializer.SerializeToUtf8Bytes(withdrawalEvent);

            await channel.BasicPublishAsync(
                _options.Exchange, _options.RoutingKey, mandatory: true, properties, body, cancellationToken);
        }
        catch
        {
            // Drop the connection so the next attempt rebuilds it from scratch.
            await CloseAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await CloseAsync();

        _connection = await RabbitMqTopology.CreateFactory(_options).CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await RabbitMqTopology.DeclareAsync(_channel, _options, cancellationToken);
        return _channel;
    }

    private async Task CloseAsync()
    {
        try
        {
            if (_channel is not null) await _channel.DisposeAsync();
            if (_connection is not null) await _connection.DisposeAsync();
        }
        catch
        {
            // Already broken; nothing useful to do.
        }

        _channel = null;
        _connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _gate.Dispose();
    }
}

internal static class RabbitMqTopology
{
    public static ConnectionFactory CreateFactory(RabbitMqOptions options) => new()
    {
        HostName = options.HostName,
        Port = options.Port,
        UserName = options.UserName,
        Password = options.Password,
        VirtualHost = options.VirtualHost,
    };

    public static async Task DeclareAsync(IChannel channel, RabbitMqOptions options, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(options.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(options.Queue, options.Exchange, options.RoutingKey, cancellationToken: cancellationToken);
    }
}
