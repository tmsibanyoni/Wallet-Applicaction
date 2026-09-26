using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WalletApp.Application.Abstractions;
using WalletApp.Domain;
using WalletApp.Domain.Abstractions;
using WalletApp.Domain.Exceptions;

namespace WalletApp.Application.Wallets;

/// <summary>
/// Orchestrates the wallet use cases. Business invariants (sufficient funds, non-negative
/// balance) live on <see cref="Wallet"/> itself; this class is responsible for loading/saving
/// the aggregate, retrying on optimistic-concurrency conflicts, and publishing the resulting
/// event once the change is durably committed.
/// </summary>
public sealed class WalletService : IWalletService
{
    private readonly IWalletRepository _walletRepository;
    private readonly IWithdrawalEventBus _eventBus;
    private readonly ILogger<WalletService> _logger;
    private readonly int _maxConcurrencyRetries;

    public WalletService(
        IWalletRepository walletRepository,
        IWithdrawalEventBus eventBus,
        IOptions<WalletServiceOptions> options,
        ILogger<WalletService> logger)
    {
        _walletRepository = walletRepository;
        _eventBus = eventBus;
        _logger = logger;
        _maxConcurrencyRetries = options.Value.MaxConcurrencyRetries;
    }

    public async Task<BalanceResponse> GetBalanceAsync(int walletId, CancellationToken cancellationToken = default)
    {
        var wallet = await _walletRepository.GetByIdAsync(walletId, cancellationToken)
            ?? throw new WalletNotFoundException(walletId);

        return new BalanceResponse(wallet.Id, wallet.Balance, wallet.Currency);
    }

    public async Task<WithdrawResult> WithdrawAsync(int walletId, decimal amount, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var wallet = await _walletRepository.GetByIdAsync(walletId, cancellationToken)
                ?? throw new WalletNotFoundException(walletId);

            // Throws InsufficientFundsException / ArgumentOutOfRangeException without touching storage.
            var withdrawalEvent = wallet.Withdraw(amount);

            try
            {
                await _walletRepository.SaveWithdrawalAsync(wallet, withdrawalEvent, cancellationToken);
            }
            catch (ConcurrencyConflictException) when (attempt < _maxConcurrencyRetries)
            {
                _logger.LogWarning(
                    "Concurrency conflict withdrawing from wallet {WalletId} on attempt {Attempt}; retrying with a fresh read.",
                    walletId, attempt);
                continue;
            }

            await PublishBestEffortAsync(withdrawalEvent, cancellationToken);

            return new WithdrawResult(
                withdrawalEvent.EventId,
                withdrawalEvent.WalletId,
                withdrawalEvent.Amount,
                withdrawalEvent.BalanceAfter,
                withdrawalEvent.Currency,
                withdrawalEvent.OccurredAtUtc);
        }
    }

    private async Task PublishBestEffortAsync(WithdrawalCompleted withdrawalEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _eventBus.PublishAsync(withdrawalEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            // The event is already durably persisted by SaveWithdrawalAsync, so a live-bus
            // failure must not fail an otherwise-successful withdrawal.
            _logger.LogError(ex, "Failed to publish withdrawal event {EventId} to the live event bus.", withdrawalEvent.EventId);
        }
    }
}
