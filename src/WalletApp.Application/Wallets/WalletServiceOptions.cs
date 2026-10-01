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
    public int MaxConcurrencyRetries { get; set; }

    /// <summary>Longest idempotency key accepted, matching the database column.</summary>
    public int MaxIdempotencyKeyLength { get; set; }

    /// <summary>How many history rows to return when the caller does not say.</summary>
    public int HistoryDefaultPageSize { get; set; }

    /// <summary>The most history rows a caller may ask for in one request.</summary>
    public int HistoryMaxPageSize { get; set; }
}
