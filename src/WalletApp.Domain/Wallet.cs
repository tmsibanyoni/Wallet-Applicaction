using WalletApp.Domain.Exceptions;

namespace WalletApp.Domain;

/// <summary>
/// Aggregate root for a single wallet. All balance mutations go through this type so the
/// "never negative" invariant is enforced in exactly one place, regardless of caller.
/// </summary>
public class Wallet
{
    public int Id { get; private set; }
    public string OwnerName { get; private set; } = string.Empty;
    public decimal Balance { get; private set; }
    public string Currency { get; private set; } = "USD";

    /// <summary>The balance as a <see cref="Money"/> value (not persisted separately; derived from Balance + Currency).</summary>
    public Money CurrentBalance => new(Balance, Currency);

    /// <summary>
    /// Optimistic concurrency token. Incremented on every state change and mapped as an
    /// EF Core concurrency token so two concurrent withdrawals can never both apply
    /// against the same balance snapshot.
    /// </summary>
    public long Version { get; private set; }

    // EF Core materializes entities via this constructor + reflection over the private setters above.
    private Wallet()
    {
    }

    public Wallet(int id, string ownerName, decimal initialBalance, string currency = "USD")
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Wallet id must be positive.");
        }

        if (string.IsNullOrWhiteSpace(ownerName))
        {
            throw new ArgumentException("Owner name is required.", nameof(ownerName));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        if (initialBalance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialBalance), initialBalance, "Initial balance cannot be negative.");
        }

        var opening = new Money(initialBalance, currency);

        Id = id;
        OwnerName = ownerName;
        Balance = opening.Amount;
        Currency = opening.Currency;
        Version = 0;
    }

    /// <summary>
    /// Withdraws <paramref name="amount"/> from the wallet.
    /// Succeeds only when funds are sufficient; the balance is updated atomically with this call
    /// and never allowed to go negative. Returns the event describing the completed withdrawal so
    /// the caller can publish it - the wallet never publishes events itself, it only describes them.
    /// </summary>
    public WithdrawalCompleted Withdraw(Money amount)
    {
        var balance = CurrentBalance;

        if (!string.Equals(amount.Currency, Currency, StringComparison.Ordinal))
        {
            throw new CurrencyMismatchException(Currency, amount.Currency);
        }

        if (amount.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount.Amount, "Withdrawal amount must be greater than zero.");
        }

        if (amount.IsGreaterThan(balance))
        {
            throw new InsufficientFundsException(Id, Balance, amount.Amount);
        }

        Balance = balance.Subtract(amount).Amount;
        Version++;

        return new WithdrawalCompleted(
            EventId: Guid.NewGuid(),
            WalletId: Id,
            Amount: amount.Amount,
            BalanceAfter: Balance,
            Currency: Currency,
            OccurredAtUtc: DateTimeOffset.UtcNow);
    }

    /// <summary>Convenience overload for callers that already know the amount is in the wallet's own currency.</summary>
    public WithdrawalCompleted Withdraw(decimal amount) => Withdraw(new Money(amount, Currency));
}
