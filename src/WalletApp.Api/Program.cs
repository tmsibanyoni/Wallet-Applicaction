using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using WalletApp.Api.Controllers;
using WalletApp.Api.Infrastructure;
using WalletApp.Application.Abstractions;
using WalletApp.Application.Wallets;
using WalletApp.Domain.Abstractions;
using WalletApp.Infrastructure.Events;
using WalletApp.Infrastructure.Persistence;
using WalletApp.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

const string AngularDevCorsPolicy = "AngularDev";
const string HealthPath = "/health";
const string ReadyHealthTag = "ready";

// Services -------------------------------------------------------------

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Wallet API",
        Version = "v1",
        Description = "Get a wallet's balance and withdraw funds from it.",
    });
});

builder.Services.AddDbContext<WalletDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("WalletDb")));

builder.Services.AddScoped<IWalletRepository, EfWalletRepository>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.Configure<WalletServiceOptions>(builder.Configuration.GetSection(WalletServiceOptions.SectionName));
builder.Services.Configure<WalletSeedOptions>(builder.Configuration.GetSection(WalletSeedOptions.SectionName));

builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
builder.Services.Configure<EventBusOptions>(builder.Configuration.GetSection(EventBusOptions.SectionName));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));

// The transport is chosen when the bus is first needed (not while building the host), so settings
// supplied late - including by the integration-test host - are honoured.
builder.Services.AddSingleton<IWithdrawalEventBus>(sp =>
{
    var provider = sp.GetRequiredService<IOptions<EventBusOptions>>().Value.Provider;
    return string.Equals(provider, EventBusOptions.LogProvider, StringComparison.OrdinalIgnoreCase)
        ? ActivatorUtilities.CreateInstance<LoggingWithdrawalEventBus>(sp)
        : ActivatorUtilities.CreateInstance<RabbitMqWithdrawalEventBus>(sp);
});
builder.Services.AddHostedService<RabbitMqWithdrawalEventConsumer>();

builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularDevCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders(WalletsController.IdempotentReplayedHeader, "Retry-After"));
});

// Health -------------------------------------------------------------------

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyHealthTag]);

// Rate limiting ------------------------------------------------------------

builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection(RateLimitingOptions.SectionName));
builder.Services.AddRateLimiter();
builder.Services.AddOptions<RateLimiterOptions>()
    .Configure<IOptions<RateLimitingOptions>>((options, settings) =>
    {
        var limits = settings.Value;
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            // Probes must keep working when a client is being throttled.
            if (!limits.Enabled || context.Request.Path.StartsWithSegments(HealthPath))
            {
                return RateLimitPartition.GetNoLimiter("unlimited");
            }

            var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter($"{client}:{(isRead ? "read" : "write")}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = isRead ? limits.ReadPermitLimit : limits.WritePermitLimit,
                Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                QueueLimit = 0,
            });
        });
        options.OnRejected = async (context, cancellationToken) =>
        {
            var http = context.HttpContext;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
            }

            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimiting")
                .LogWarning("Rate limit hit by {Client} on {Method} {Path}", http.Connection.RemoteIpAddress, http.Request.Method, http.Request.Path);

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = "You are sending requests too quickly. Please wait a moment and try again.",
                Instance = http.Request.Path,
            };
            problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? http.TraceIdentifier;

            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await http.Response.WriteAsJsonAsync(problem, cancellationToken);
        };
    });

// Tracing ------------------------------------------------------------------

var telemetry = builder.Configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new();
if (telemetry.Enabled)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(telemetry.ServiceName))
        .WithTracing(tracing =>
        {
            tracing
                .AddSource(WalletTelemetry.SourceName)
                .AddAspNetCoreInstrumentation(o => o.Filter = context => !context.Request.Path.StartsWithSegments(HealthPath))
                .AddSqlClientInstrumentation();

            if (string.Equals(telemetry.Exporter, TelemetryOptions.ConsoleExporter, StringComparison.OrdinalIgnoreCase))
            {
                tracing.AddConsoleExporter();
            }
            else if (string.Equals(telemetry.Exporter, TelemetryOptions.OtlpExporter, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(telemetry.OtlpEndpoint))
            {
                tracing.AddOtlpExporter(o => o.Endpoint = new Uri(telemetry.OtlpEndpoint));
            }
        });
}

var app = builder.Build();

// Database setup: apply pending migrations and guarantee the configured seed wallet exists.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
    var seedOptions = scope.ServiceProvider.GetRequiredService<IOptions<WalletSeedOptions>>().Value;
    await WalletDbInitializer.InitializeAsync(dbContext, seedOptions);
}

// Pipeline ---------------------------------------------------------------

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Wallet API v1");
});

app.UseExceptionHandler();

app.UseCors(AngularDevCorsPolicy);

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

// Liveness says the process is up; readiness also checks the database.
app.MapHealthChecks($"{HealthPath}/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks($"{HealthPath}/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyHealthTag) });

app.Run();

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can bootstrap the app in integration tests.</summary>
public partial class Program
{
}
