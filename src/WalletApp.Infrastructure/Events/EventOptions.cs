namespace WalletApp.Infrastructure.Events;

/// <summary>Settings for <see cref="OutboxDispatcher"/>, bound from the "Outbox" section.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Turns the background relay on or off (integration tests drive it by hand).</summary>
    public bool Enabled { get; set; }

    public int PollIntervalMs { get; set; }

    public int BatchSize { get; set; }
}

/// <summary>Chooses the transport the outbox relays to, from the "EventBus" section.</summary>
public sealed class EventBusOptions
{
    public const string SectionName = "EventBus";

    public const string RabbitMqProvider = "RabbitMq";
    public const string LogProvider = "Log";

    public string Provider { get; set; } = string.Empty;
}

/// <summary>Broker connection and topology settings, bound from the "RabbitMq" section.</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = string.Empty;
    public int Port { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public string Queue { get; set; } = string.Empty;
}
