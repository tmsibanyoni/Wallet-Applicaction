using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WalletApp.Api.Infrastructure;
using WalletApp.Application.Abstractions;
using WalletApp.Application.Wallets;
using WalletApp.Domain.Abstractions;
using WalletApp.Infrastructure.Events;
using WalletApp.Infrastructure.Persistence;
using WalletApp.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

const string AngularDevCorsPolicy = "AngularDev";

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

builder.Services.AddSingleton<InProcessWithdrawalEventBus>();
builder.Services.AddSingleton<IWithdrawalEventBus>(sp => sp.GetRequiredService<InProcessWithdrawalEventBus>());
builder.Services.AddHostedService<WithdrawalEventLoggingConsumer>();

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularDevCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

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

app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can bootstrap the app in integration tests.</summary>
public partial class Program
{
}
