using System.Globalization;
using BuildingBlocks.Messaging.Extensions;
using BuildingBlocks.Telemetry.Extensions;
using EconomyService.Auth;
using EconomyService.Endpoints;
using EconomyService.Extensions;
using EconomyService.Infrastructure;
using EconomyService.Options;
using EconomyService.Persistence;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .WriteToPlatformLoki(context.Configuration, "economy-service"));

// Comfortably below the Helm chart's terminationGracePeriodSeconds default (30s,
// infra/helm/gaming-backend-platform/values.yaml) so the host always finishes an
// in-flight request or outbox dispatch cycle and exits on its own, instead of
// Kubernetes cutting it short with SIGKILL once the grace period runs out.
builder.Host.ConfigureHostOptions(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddEconomyExceptionHandling();
builder.Services.AddEconomyHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddEconomyPersistence(builder.Configuration);
builder.Services.AddEconomyAuthentication(builder.Configuration);
builder.Services.AddEconomyServices(builder.Configuration);
builder.Services.AddRabbitMqEventBus(builder.Configuration);
builder.Services.AddOutbox<EconomyDbContext>();
builder.Services.AddOutboxDispatcher<EconomyDbContext>(builder.Configuration);
builder.Services.AddDeduplicatingEventConsumer();
builder.Services.AddWelcomeGrantConsumer();
builder.Services.AddPlatformTelemetry(builder.Configuration, "economy-service");
builder.Services.AddScoped<DevelopmentSeeder>();

var app = builder.Build();

// Blocking, one-time, before the app accepts any requests -- the same principle already
// applied to ValidateOnStart for configuration: this service shouldn't finish starting if
// it can't reach the one dependency (Identity's published keys) it needs to validate a
// single incoming token.
await app.Services.GetRequiredService<IJwksKeyCache>().RefreshAsync(CancellationToken.None);

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<EnduserIdMiddleware>();

if (app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.ExposeOpenApi)
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

if (app.Services.GetRequiredService<IOptions<SeedingOptions>>().Value.Enabled)
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
    await seeder.SeedAsync();
}

app.MapHealthEndpoints();
app.MapCurrencyEndpoints();
app.MapBalanceEndpoints();
app.MapTransactionEndpoints();
app.MapConversionEndpoints();

app.Run();

public partial class Program;
