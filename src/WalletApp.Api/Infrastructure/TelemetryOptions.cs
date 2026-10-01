namespace WalletApp.Api.Infrastructure;

/// <summary>OpenTelemetry tracing settings, bound from the "Telemetry" configuration section.</summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    public const string ConsoleExporter = "Console";
    public const string OtlpExporter = "Otlp";

    public bool Enabled { get; set; } = true;

    public string ServiceName { get; set; } = "wallet-api";

    /// <summary>"Console" prints finished spans to stdout, "Otlp" sends them to <see cref="OtlpEndpoint"/>, anything else exports nothing.</summary>
    public string Exporter { get; set; } = ConsoleExporter;

    public string? OtlpEndpoint { get; set; }
}
