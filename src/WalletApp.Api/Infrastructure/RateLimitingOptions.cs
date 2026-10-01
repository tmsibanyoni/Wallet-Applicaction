namespace WalletApp.Api.Infrastructure;

/// <summary>Per-client request limits, bound from the "RateLimiting" configuration section.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>Requests a single client may make in one window to read endpoints (GET).</summary>
    public int ReadPermitLimit { get; set; } = 120;

    /// <summary>Requests a single client may make in one window to write endpoints (POST and the like).</summary>
    public int WritePermitLimit { get; set; } = 30;

    public int WindowSeconds { get; set; } = 60;
}
