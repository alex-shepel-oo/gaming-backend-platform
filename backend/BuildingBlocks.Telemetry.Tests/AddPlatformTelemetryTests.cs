using AwesomeAssertions;
using BuildingBlocks.Telemetry.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace BuildingBlocks.Telemetry.Tests;

public class AddPlatformTelemetryTests
{
    private static IConfiguration BuildConfiguration(string otlpEndpoint = "http://localhost:4317") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telemetry:OtlpEndpoint"] = otlpEndpoint,
                ["Telemetry:LokiEndpoint"] = "http://localhost:3100",
            })
            .Build();

    [Fact]
    public void AddPlatformTelemetry_RegistersTracerAndMeterProviders()
    {
        var services = new ServiceCollection();

        services.AddPlatformTelemetry(BuildConfiguration(), "test-service");
        using var provider = services.BuildServiceProvider();

        provider.GetService<TracerProvider>().Should().NotBeNull();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddPlatformTelemetry_AttachesServiceNameAndDeploymentEnvironmentToTheResource()
    {
        var services = new ServiceCollection();

        services.AddPlatformTelemetry(BuildConfiguration(), "test-service");
        using var provider = services.BuildServiceProvider();

        var resource = provider.GetRequiredService<TracerProvider>().GetResource();

        resource.Attributes.Should().Contain(new KeyValuePair<string, object>("service.name", "test-service"));
        resource.Attributes.Select(attribute => attribute.Key).Should().Contain("deployment.environment");
    }

    [Fact]
    public async Task AddPlatformTelemetry_HostStartsAndStopsWhenTheCollectorIsUnreachable()
    {
        // Port 1 has no listener and never will -- the OTLP exporter batches and retries on its own
        // background timer, so a host with nowhere to actually deliver spans/metrics still has to
        // start and stop cleanly rather than throwing during either transition.
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddPlatformTelemetry(
                BuildConfiguration(otlpEndpoint: "http://127.0.0.1:1"), "test-service"))
            .Build();

        var act = async () =>
        {
            await host.StartAsync();
            await host.StopAsync();
        };

        await act.Should().NotThrowAsync();
    }
}
