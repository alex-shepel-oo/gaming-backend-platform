using System.Globalization;
using BuildingBlocks.Telemetry.Extensions;
using EmailService.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EmailService;

public static class EmailServiceHostBuilder
{
    public static IHostBuilder Create(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog((context, configuration) => configuration
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
                .WriteToPlatformLoki(context.Configuration, "email-service"))
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(TimeProvider.System);
                services.AddEmailSending(context.Configuration);
                services.AddEmailMessaging(context.Configuration);
                services.AddPlatformTelemetry(context.Configuration, "email-service");
            });
}
