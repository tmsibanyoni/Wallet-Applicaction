namespace WalletApp.Infrastructure.Persistence;

/// <summary>
/// The wallet the app guarantees exists on startup, bound from the "WalletSeed" section of
/// configuration (see appsettings.json). A wallet-creation endpoint is out of scope, so this is
/// how the app satisfies "starts with at least one wallet with a known positive balance" without
/// hard-coding that data into application code.
/// </summary>
public sealed class WalletSeedOptions
{
    public const string SectionName = "WalletSeed";

    public Guid WalletId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
    public string Currency { get; set; } = string.Empty;
}
