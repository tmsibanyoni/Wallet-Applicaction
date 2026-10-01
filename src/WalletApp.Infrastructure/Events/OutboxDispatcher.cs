using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WalletApp.Application.Abstractions;
using WalletApp.Infrastructure.Persistence;

namespace WalletApp.Infrastructure.Events;

/// <summary>
/// Relays undelivered <see cref="WithdrawalEventRecord"/> rows to the <see cref="IWithdrawalEventBus"/>
/// in the order they happened and stamps each one once the bus has accepted it. If the broker is
/// down the rows simply stay pending and are retried on the next poll, so no event is lost.
/// </summary>
public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWithdrawalEventBus _eventBus;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IWithdrawalEventBus eventBus,
        IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Outbox dispatcher is disabled.");
            return;
        }

        var delay = TimeSpan.FromMilliseconds(_options.PollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchPendingAsync(stoppingToken);

                // A full batch means there is probably more waiting, so go straight back for it.
                if (dispatched >= _options.BatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatch pass failed; will retry in {Delay}.", delay);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs one pass and returns how many events were delivered.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        var pending = await dbContext.WithdrawalEvents
            .Where(e => e.DispatchedAtUtc == null)
            .OrderBy(e => e.OccurredAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        var dispatched = 0;

        foreach (var record in pending)
        {
            try
            {
                await _eventBus.PublishAsync(record.ToEvent(), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Stop here so later events never overtake this one; it is retried next pass.
                _logger.LogWarning(ex, "Could not publish withdrawal event {EventId}; it stays pending.", record.Id);
                break;
            }

            record.MarkDispatched(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            dispatched++;
        }

        return dispatched;
    }
}
