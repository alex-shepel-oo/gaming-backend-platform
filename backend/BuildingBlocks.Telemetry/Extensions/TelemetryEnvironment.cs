using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Telemetry.Extensions;

/// <summary>
/// Web apps set ASPNETCORE_ENVIRONMENT; Platform.Worker's generic Host sets DOTNET_ENVIRONMENT.
/// </summary>
internal static class TelemetryEnvironment
{
    public static string ResolveName(IConfiguration configuration) =>
        configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"] ?? Environments.Production;
}
