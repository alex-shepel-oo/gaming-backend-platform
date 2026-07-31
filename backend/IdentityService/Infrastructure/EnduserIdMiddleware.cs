using System.Diagnostics;
using Microsoft.IdentityModel.JsonWebTokens;
using Serilog.Context;

namespace IdentityService.Infrastructure;

// Mirrors CorrelationIdMiddleware's exact LogContext.PushProperty shape, but has to run after
// UseAuthentication()/UseAuthorization() rather than before them - there is no HttpContext.User
// claim to read until authentication has actually populated it.
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

        // "enduser.id" - the OTel semantic convention attribute name - doubles as the Serilog
        // property name too, deliberately: the whole point is filtering the same identifier
        // across both traces and logs, which is easiest when the field is spelled identically
        // in both places rather than "EnduserId" here and "enduser.id" on the Activity.
        Activity.Current?.SetTag("enduser.id", userId);

        using (LogContext.PushProperty("enduser.id", userId))
        {
            await next(context);
        }
    }
}
