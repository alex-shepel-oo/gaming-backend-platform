using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace EconomyService.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
        });
    }
}
