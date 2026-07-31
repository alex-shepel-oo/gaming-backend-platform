using System.Globalization;
using System.Text.Json.Serialization;
using BuildingBlocks.Messaging.Extensions;
using BuildingBlocks.Telemetry.Extensions;
using IdentityService.Endpoints;
using IdentityService.Extensions;
using IdentityService.Infrastructure;
using IdentityService.Persistence;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .WriteToPlatformLoki(context.Configuration, "identity-service"));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddIdentityExceptionHandling();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddIdentityPersistence(builder.Configuration);
builder.Services.AddIdentityHealthChecks();
builder.Services.AddIdentityServices(builder.Configuration);
builder.Services.AddIdentityAuthentication();
builder.Services.AddIdentityRateLimiting(builder.Configuration);
builder.Services.AddIdentityEmail(builder.Configuration);
builder.Services.AddRabbitMqEventBus(builder.Configuration);
builder.Services.AddOutbox<IdentityDbContext>();
builder.Services.AddOutboxDispatcher<IdentityDbContext>(builder.Configuration);
builder.Services.AddPlatformTelemetry(builder.Configuration, "identity-service");
builder.Services.AddScoped<DevelopmentSeeder>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseRateLimiter();
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
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapGameEndpoints();
app.MapRolePermissionEndpoints();
app.MapJwksEndpoints();

app.Run();

public partial class Program;
