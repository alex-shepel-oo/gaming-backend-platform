using System.Diagnostics;
using BuildingBlocks.Telemetry;
using Microsoft.IdentityModel.JsonWebTokens;
using Serilog.Context;

namespace NotificationService.Infrastructure;

// Mirrors CorrelationIdMiddleware's exact LogContext.PushProperty shape, but has to run after
// UseAuthentication()/UseAuthorization() rather than before them - there is no HttpContext.User
// claim to read until authentication has actually populated it. This service has no authenticated
// REST endpoints of its own, but the SignalR hub's negotiate/connect requests go through this same
// pipeline and are authenticated (NotificationHub is [Authorize]), so it applies here too.
public sealed class EnduserIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (userId is null)
        {
            await next(context);
            return;
        }

        // Doubles as the Serilog property name too, deliberately: the whole point is filtering
        // the same identifier across both traces and logs, which is easiest when the field is
        // spelled identically in both places.
        Activity.Current?.SetTag(OtelConventions.EnduserId, userId);

        using (LogContext.PushProperty(OtelConventions.EnduserId, userId))
        {
            await next(context);
        }
    }
}
