using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WalletApp.Domain;
using WalletApp.Domain.Abstractions;
using WalletApp.Domain.Exceptions;

namespace WalletApp.Application.Wallets;

/// <summary>
/// Orchestrates the wallet use cases. Business invariants (sufficient funds, non-negative
/// balance) live on <see cref="Wallet"/> itself; this class is responsible for loading/saving
/// the aggregate, retrying on optimistic-concurrency conflicts and replaying requests that carry
/// an idempotency key already used. The resulting event is stored with the balance change (outbox)
/// and delivered to the broker by a background dispatcher.
/// </summary>
public sealed class WalletService : IWalletService
{
    private readonly IWalletRepository _walletRepository;
    private readonly ILogger<WalletService> _logger;
    private readonly int _maxConcurrencyRetries;
    private readonly int _maxIdempotencyKeyLength;

    public WalletService(
        IWalletRepository walletRepository,
        IOptions<WalletServiceOptions> options,
        ILogger<WalletService> logger)
    {
        _walletRepository = walletRepository;
        _logger = logger;
        _maxConcurrencyRetries = options.Value.MaxConcurrencyRetries;
        _maxIdempotencyKeyLength = options.Value.MaxIdempotencyKeyLength;
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
        var key = NormaliseKey(command.IdempotencyKey);

        for (var attempt = 1; ; attempt++)
        {
            var wallet = await _walletRepository.GetByIdAsync(walletId, cancellationToken)
                ?? throw new WalletNotFoundException(walletId);

            if (key is not null)
            {
                var previous = await _walletRepository.FindWithdrawalByKeyAsync(walletId, key, cancellationToken);
                if (previous is not null)
                {
                    return Replay(previous, command, key);
                }
            }

            // Throws InsufficientFundsException / ArgumentOutOfRangeException without touching storage.
            var withdrawalEvent = wallet.Withdraw(new Money(command.Amount, command.Currency ?? wallet.Currency))
                with { IdempotencyKey = key };

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
            catch (DuplicateIdempotencyKeyException) when (attempt < _maxConcurrencyRetries)
            {
                // A parallel request with the same key won the race; the next pass finds its result.
                _logger.LogInformation(
                    "Idempotency key already stored for wallet {WalletId}; returning the earlier result.", walletId);
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

    private string? NormaliseKey(string? key)
    {
        if (key is null)
        {
            return null;
        }

        var trimmed = key.Trim();

        if (trimmed.Length == 0 || trimmed.Length > _maxIdempotencyKeyLength)
        {
            throw new ArgumentException(
                $"The Idempotency-Key must be between 1 and {_maxIdempotencyKeyLength} characters.", nameof(key));
        }

        return trimmed;
    }

    private static WithdrawResult Replay(WithdrawalCompleted previous, WithdrawCommand command, string key)
    {
        var sameCurrency = command.Currency is null
            || string.Equals(command.Currency.Trim(), previous.Currency, StringComparison.OrdinalIgnoreCase);

        if (command.Amount != previous.Amount || !sameCurrency)
        {
            throw new IdempotencyKeyReuseException(key);
        }

        return new WithdrawResult(
            previous.EventId,
            previous.WalletId,
            previous.Amount,
            previous.BalanceAfter,
            previous.Currency,
            previous.OccurredAtUtc,
            Replayed: true);
    }
}
