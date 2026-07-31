using System.Globalization;
using BuildingBlocks.Telemetry.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Serilog;

namespace Platform.Worker;

public static class WorkerHostBuilder
{
    public static IHostBuilder Create(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog((context, configuration) => configuration
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
                .WriteToPlatformLoki(context.Configuration, "platform-worker"))
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(TimeProvider.System);
                services.AddQuartz();
                services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
                services.AddPlatformTelemetry(context.Configuration, "platform-worker");
            });
}
