using System.Globalization;
using BuildingBlocks.Messaging.Extensions;
using EconomyService.Endpoints;
using EconomyService.Extensions;
using EconomyService.Infrastructure;
using EconomyService.Persistence;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddEconomyExceptionHandling();
builder.Services.AddEconomyHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddEconomyPersistence(builder.Configuration);
builder.Services.AddEconomyAuthentication(builder.Configuration);
builder.Services.AddEconomyServices();
builder.Services.AddRabbitMqEventBus(builder.Configuration);
builder.Services.AddOutbox<EconomyDbContext>();
builder.Services.AddOutboxDispatcher<EconomyDbContext>(builder.Configuration);
builder.Services.AddDeduplicatingEventConsumer();
builder.Services.AddScoped<DevelopmentSeeder>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
        await seeder.SeedAsync();
    }
}

app.MapHealthEndpoints();
app.MapCurrencyEndpoints();
app.MapBalanceEndpoints();
app.MapTransactionEndpoints();
app.MapConversionEndpoints();

app.Run();

public partial class Program;
