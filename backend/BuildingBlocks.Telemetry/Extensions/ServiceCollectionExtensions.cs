using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Telemetry.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers ASP.NET Core, HttpClient and EF Core instrumentation, exporting both traces and
    /// metrics via OTLP to the otel-collector. Resource attributes (<c>service.name</c>,
    /// <c>deployment.environment</c>) are attached once here so every span and metric point carries
    /// them, rather than each service re-deriving that per call site.
    /// </summary>
    public static IServiceCollection AddPlatformTelemetry(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The OpenTelemetry builder callbacks below run eagerly, at registration time, before the
        // container is built -- they can't resolve IOptions<TelemetryOptions> the way the rest of
        // the app does. Bound directly from configuration instead; AddOptions above still runs
        // ValidateOnStart so a missing/invalid section fails host startup the same way every other
        // options class in this solution does.
        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
            ?? new TelemetryOptions();
        var otlpEndpoint = new Uri(options.OtlpEndpoint);
        var environmentName = TelemetryEnvironment.ResolveName(configuration);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment", environmentName)]))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                // BuildingBlocks.Messaging's own ActivitySource (outbox publish / inbox-and-consumer
                // process spans) - a custom source is invisible to the SDK until named explicitly
                // like this, unlike the three well-known ones above. Registered here unconditionally
                // rather than per-service: harmless for a service that never touches messaging, and
                // one fewer thing for each Program.cs to have to remember to wire up.
                .AddSource("BuildingBlocks.Messaging")
                .AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint));

        return services;
    }
}
