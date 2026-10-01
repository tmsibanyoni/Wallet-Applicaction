using System.Diagnostics;

namespace WalletApp.Infrastructure.Events;

/// <summary>Source for the custom trace spans the app creates (the API subscribes to it by name).</summary>
public static class WalletTelemetry
{
    public const string SourceName = "WalletApp";

    public static readonly ActivitySource Source = new(SourceName);
}
