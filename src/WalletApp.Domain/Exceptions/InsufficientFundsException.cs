using System.Globalization;

namespace WalletApp.Domain.Exceptions;

/// <summary>Thrown when a withdrawal is attempted for more than the wallet's current balance.</summary>
public sealed class InsufficientFundsException : Exception
{
    public Guid WalletId { get; }
    public decimal CurrentBalance { get; }
    public decimal RequestedAmount { get; }

    public InsufficientFundsException(Guid walletId, decimal currentBalance, decimal requestedAmount)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "Wallet '{0}' has insufficient funds: balance is {1}, requested {2}.",
            walletId, currentBalance, requestedAmount))
    {
        WalletId = walletId;
        CurrentBalance = currentBalance;
        RequestedAmount = requestedAmount;
    }
}
