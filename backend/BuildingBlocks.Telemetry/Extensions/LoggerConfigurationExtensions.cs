using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

namespace BuildingBlocks.Telemetry.Extensions;

public static class LoggerConfigurationExtensions
{
    /// <summary>
    /// Adds the Grafana Loki sink alongside whatever sinks (Console, etc.) the caller already
    /// configured. Called from inside each service's <c>UseSerilog</c> callback, which runs before
    /// the DI container exists: like <see cref="ServiceCollectionExtensions.AddPlatformTelemetry"/>,
    /// this binds <see cref="TelemetryOptions"/> straight from configuration rather than resolving
    /// IOptions. Every log line is tagged with <c>service_name</c> and <c>environment</c> so
    /// Loki/Grafana can filter by both without parsing the message body.
    /// </summary>
    public static LoggerConfiguration WriteToPlatformLoki(
        this LoggerConfiguration loggerConfiguration, IConfiguration configuration, string serviceName)
    {
        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
            ?? new TelemetryOptions();
        var environmentName = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? Environments.Production;

        return loggerConfiguration.WriteTo.GrafanaLoki(
            options.LokiEndpoint,
            [
                new LokiLabel { Key = "service_name", Value = serviceName },
                new LokiLabel { Key = "environment", Value = environmentName },
            ]);
    }
}
