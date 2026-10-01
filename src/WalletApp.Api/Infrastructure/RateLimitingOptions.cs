namespace WalletApp.Api.Infrastructure;

/// <summary>Per-client request limits, bound from the "RateLimiting" configuration section.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; }

    /// <summary>Requests a single client may make in one window to read endpoints (GET).</summary>
    public int ReadPermitLimit { get; set; }

    /// <summary>Requests a single client may make in one window to write endpoints (POST and the like).</summary>
    public int WritePermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}
