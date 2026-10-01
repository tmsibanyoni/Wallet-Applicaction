using WalletApp.Domain.Exceptions;

namespace WalletApp.Domain;

/// <summary>
/// An amount of a specific currency. Keeps the amount and its currency together so arithmetic
/// across currencies fails loudly instead of silently mixing units, and rejects amounts finer
/// than the storage precision (so what is validated is exactly what is persisted).
/// </summary>
public readonly record struct Money
{
    /// <summary>Decimal places kept for every currency (matches the decimal(18,2) database columns).</summary>
    public const int MinorUnitDigits = 2;

    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        var normalised = currency?.Trim().ToUpperInvariant();
        if (normalised is not { Length: 3 } || !normalised.All(char.IsAsciiLetter))
        {
            throw new ArgumentException("Currency must be a three-letter ISO 4217 code.", nameof(currency));
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");
        }

        if (decimal.Round(amount, MinorUnitDigits) != amount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, $"Amount cannot have more than {MinorUnitDigits} decimal places.");
        }

        Amount = amount;
        Currency = normalised;
    }

    public bool IsZero => Amount == 0;

    public bool IsGreaterThan(Money other)
    {
        EnsureSameCurrency(other);
        return Amount > other.Amount;
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new CurrencyMismatchException(Currency, other.Currency);
        }
    }
}
