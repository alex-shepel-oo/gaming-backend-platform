using System.Globalization;
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
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddNotificationExceptionHandling();
builder.Services.AddNotificationMessaging(builder.Configuration);
builder.Services.AddNotificationHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddNotificationAuthentication(builder.Configuration);
builder.Services.AddNotificationSignalR();

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthEndpoints();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

public partial class Program;
