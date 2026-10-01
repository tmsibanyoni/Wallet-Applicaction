using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WalletApp.Domain;
using WalletApp.Domain.Abstractions;
using WalletApp.Domain.Exceptions;

namespace WalletApp.Application.Wallets;

/// <summary>
/// Orchestrates the wallet use cases. Business invariants (sufficient funds, non-negative
/// balance) live on <see cref="Wallet"/> itself; this class is responsible for loading/saving
/// the aggregate and retrying on optimistic-concurrency conflicts. The resulting event is stored
/// with the balance change (outbox) and delivered to the broker by a background dispatcher.
/// </summary>
public sealed class WalletService : IWalletService
{
    private readonly IWalletRepository _walletRepository;
    private readonly ILogger<WalletService> _logger;
    private readonly int _maxConcurrencyRetries;

    public WalletService(
        IWalletRepository walletRepository,
        IOptions<WalletServiceOptions> options,
        ILogger<WalletService> logger)
    {
        _walletRepository = walletRepository;
        _logger = logger;
        _maxConcurrencyRetries = options.Value.MaxConcurrencyRetries;
    }

    public async Task<BalanceResponse> GetBalanceAsync(int walletId, CancellationToken cancellationToken = default)
    {
        var wallet = await _walletRepository.GetByIdAsync(walletId, cancellationToken)
            ?? throw new WalletNotFoundException(walletId);

        return new BalanceResponse(wallet.Id, wallet.Balance, wallet.Currency);
    }

    public async Task<WithdrawResult> WithdrawAsync(WithdrawCommand command, CancellationToken cancellationToken = default)
    {
        var walletId = command.WalletId;

        for (var attempt = 1; ; attempt++)
        {
            var wallet = await _walletRepository.GetByIdAsync(walletId, cancellationToken)
                ?? throw new WalletNotFoundException(walletId);

            // Throws InsufficientFundsException / ArgumentOutOfRangeException without touching storage.
            var withdrawalEvent = wallet.Withdraw(new Money(command.Amount, command.Currency ?? wallet.Currency));

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

            return new WithdrawResult(
                withdrawalEvent.EventId,
                withdrawalEvent.WalletId,
                withdrawalEvent.Amount,
                withdrawalEvent.BalanceAfter,
                withdrawalEvent.Currency,
                withdrawalEvent.OccurredAtUtc);
        }
    }
}
