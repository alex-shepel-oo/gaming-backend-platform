using System.Globalization;
using BuildingBlocks.Telemetry.Extensions;
using NotificationService.Auth;
using NotificationService.Endpoints;
using NotificationService.Extensions;
using NotificationService.Hubs;
using NotificationService.Infrastructure;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .WriteToPlatformLoki(context.Configuration, "notification-service"));

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddNotificationExceptionHandling();
builder.Services.AddNotificationMessaging(builder.Configuration);
builder.Services.AddNotificationHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddNotificationAuthentication(builder.Configuration);
builder.Services.AddNotificationSignalR();
builder.Services.AddPlatformTelemetry(builder.Configuration, "notification-service");

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthEndpoints();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

public partial class Program;
