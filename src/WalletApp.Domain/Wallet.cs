using WalletApp.Domain.Exceptions;

namespace WalletApp.Domain;

/// <summary>
/// Aggregate root for a single wallet. All balance mutations go through this type so the
/// "never negative" invariant is enforced in exactly one place, regardless of caller.
/// </summary>
public class Wallet
{
    public Guid Id { get; private set; }
    public string OwnerName { get; private set; } = string.Empty;
    public decimal Balance { get; private set; }
    public string Currency { get; private set; } = "USD";

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

    public Wallet(Guid id, string ownerName, decimal initialBalance, string currency = "USD")
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Wallet id must not be empty.", nameof(id));
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

        Id = id;
        OwnerName = ownerName;
        Balance = initialBalance;
        Currency = currency;
        Version = 0;
    }

    /// <summary>
    /// Withdraws <paramref name="amount"/> from the wallet.
    /// Succeeds only when funds are sufficient; the balance is updated atomically with this call
    /// and never allowed to go negative. Returns the event describing the completed withdrawal so
    /// the caller can publish it - the wallet never publishes events itself, it only describes them.
    /// </summary>
    public WithdrawalCompleted Withdraw(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Withdrawal amount must be greater than zero.");
        }

        if (amount > Balance)
        {
            throw new InsufficientFundsException(Id, Balance, amount);
        }

        Balance -= amount;
        Version++;

        return new WithdrawalCompleted(
            EventId: Guid.NewGuid(),
            WalletId: Id,
            Amount: amount,
            BalanceAfter: Balance,
            Currency: Currency,
            OccurredAtUtc: DateTimeOffset.UtcNow);
    }
}
