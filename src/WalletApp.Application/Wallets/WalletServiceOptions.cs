namespace WalletApp.Application.Wallets;

/// <summary>Tuning knobs for <see cref="WalletService"/>, bound from the "Withdrawal" configuration section.</summary>
public sealed class WalletServiceOptions
{
    public const string SectionName = "Withdrawal";

    /// <summary>
    /// How many times to retry a withdrawal after an optimistic-concurrency conflict (see
    /// <see cref="Domain.Exceptions.ConcurrencyConflictException"/>) before giving up and letting
    /// the conflict surface to the caller as a 503.
    /// </summary>
    public int MaxConcurrencyRetries { get; set; } = 3;
}
