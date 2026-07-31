using System.ComponentModel.DataAnnotations;

namespace BuildingBlocks.Telemetry;

/// <summary>
/// Bound from the "Telemetry" configuration section. Defaults assume a bare local <c>dotnet run</c>
/// outside docker-compose -- the same convention <c>BuildingBlocks.Messaging.RabbitMqOptions</c> uses
/// for its own Host default -- docker-compose overrides both endpoints per service via
/// <c>Telemetry__OtlpEndpoint</c> / <c>Telemetry__LokiEndpoint</c> env vars pointed at the
/// otel-collector/loki container names.
/// </summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    [Required]
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";

    [Required]
    public string LokiEndpoint { get; set; } = "http://localhost:3100";
}
