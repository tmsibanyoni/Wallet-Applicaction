namespace WalletApp.Domain.Exceptions;

/// <summary>Raised when an operation mixes two different currencies.</summary>
public sealed class CurrencyMismatchException : Exception
{
    public string ExpectedCurrency { get; }
    public string ActualCurrency { get; }

    public CurrencyMismatchException(string expectedCurrency, string actualCurrency)
        : base($"Currency mismatch: the wallet holds {expectedCurrency} but {actualCurrency} was supplied.")
    {
        ExpectedCurrency = expectedCurrency;
        ActualCurrency = actualCurrency;
    }
}
