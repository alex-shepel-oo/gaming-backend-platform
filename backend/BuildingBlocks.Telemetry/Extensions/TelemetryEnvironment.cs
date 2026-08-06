using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Telemetry.Extensions;

/// <summary>
/// ASP.NET Core services set <c>ASPNETCORE_ENVIRONMENT</c>; Platform.Worker (a generic Host,
/// not a web app) sets <c>DOTNET_ENVIRONMENT</c> instead -- both are checked so the same
/// library call works unmodified in either host type. Shared by
/// <see cref="ServiceCollectionExtensions.AddPlatformTelemetry"/> and
/// <see cref="LoggerConfigurationExtensions.WriteToPlatformLoki"/>, which both need this before
/// the DI container exists.
/// </summary>
internal static class TelemetryEnvironment
{
    public static string ResolveName(IConfiguration configuration) =>
        configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"] ?? Environments.Production;
}
