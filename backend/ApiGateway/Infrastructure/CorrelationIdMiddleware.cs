using Microsoft.Extensions.Primitives;
using Serilog.Context;

namespace ApiGateway.Infrastructure;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var headerValue) &&
            !StringValues.IsNullOrEmpty(headerValue))
        {
            return headerValue.ToString();
        }

        return Guid.CreateVersion7().ToString();
    }
}
