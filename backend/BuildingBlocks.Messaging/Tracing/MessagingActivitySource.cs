using System.Diagnostics;

namespace BuildingBlocks.Messaging.Tracing;

// A custom ActivitySource is invisible to OpenTelemetry until something asks the SDK to listen to
// it by name - AddAspNetCoreInstrumentation/AddHttpClientInstrumentation/
// AddEntityFrameworkCoreInstrumentation only subscribe to their own well-known sources.
// BuildingBlocks.Telemetry's AddPlatformTelemetry adds this one explicitly via
// .AddSource(Name), or none of the spans started through Instance ever reach the collector.
public static class MessagingActivitySource
{
    public const string Name = "BuildingBlocks.Messaging";

    public static readonly ActivitySource Instance = new(Name);
}
