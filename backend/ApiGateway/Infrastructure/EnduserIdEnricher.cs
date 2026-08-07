using System.Diagnostics;
using BuildingBlocks.Telemetry;
using Microsoft.IdentityModel.JsonWebTokens;
using Serilog.Context;

namespace ApiGateway.Infrastructure;

// The gateway has no app.UseAuthentication()/UseAuthorization() of its own - Ocelot authenticates
// per-route, inside its own internal pipeline built by UseOcelot(), so HttpContext.User isn't
// populated at the point a regular ASP.NET Core middleware would run. OcelotPipelineConfiguration's
// PreAuthorizationMiddleware hook is the equivalent insertion point here: it runs immediately after
// Ocelot's own AuthenticationMiddleware and before its AuthorizationMiddleware, matching the
// "after UseAuthentication()/UseAuthorization()" placement the other services use. Factored as a
// plain static method, not a middleware class, because Ocelot's hook is a
// Func&lt;HttpContext, Func&lt;Task&gt;, Task&gt; delegate, not the RequestDelegate shape
// app.UseMiddleware&lt;T&gt;() expects - this keeps the actual enrichment logic unit-testable
// independent of that delegate shape.
public static class EnduserIdEnricher
{
    public static async Task ApplyAsync(HttpContext context, Func<Task> next)
    {
        var userId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (userId is null)
        {
            await next();
            return;
        }

        // Doubles as the Serilog property name too, deliberately: the whole point is filtering
        // the same identifier across both traces and logs, which is easiest when the field is
        // spelled identically in both places.
        Activity.Current?.SetTag(OtelConventions.EnduserId, userId);

        using (LogContext.PushProperty(OtelConventions.EnduserId, userId))
        {
            await next();
        }
    }
}
