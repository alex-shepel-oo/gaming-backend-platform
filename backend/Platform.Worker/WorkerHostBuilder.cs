using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Serilog;

namespace Platform.Worker;

public static class WorkerHostBuilder
{
    public static IHostBuilder Create(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog((_, configuration) => configuration
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture))
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton(TimeProvider.System);
                services.AddQuartz();
                services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
            });
}
