using System.Text.Json;
using AwesomeAssertions;
using EconomyService.Extensions;
using EconomyService.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace EconomyService.Tests.Unit;

[TestFixture]
public sealed class GlobalExceptionHandlerTests
{
    [Test]
    public async Task TryHandleAsync_UnhandledException_Returns500WithoutLeakingExceptionMessage()
    {
        var exception = new InvalidOperationException("connection string contains a password");

        var (statusCode, body) = await HandleAsync(exception);

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
        body.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status500InternalServerError);
        body.GetProperty("detail").GetString().Should().NotContain("password");
    }

    [Test]
    public async Task TryHandleAsync_TraceIdMatchesRequestTraceIdentifier()
    {
        var (_, body) = await HandleAsync(new InvalidOperationException("boom"), traceIdentifier: "test-correlation-id");

        body.GetProperty("traceId").GetString().Should().Be("test-correlation-id");
    }

    private static async Task<(int StatusCode, JsonElement Body)> HandleAsync(
        Exception exception, string traceIdentifier = "trace-id")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEconomyExceptionHandling();
        await using var provider = services.BuildServiceProvider();

        var handler = provider.GetServices<IExceptionHandler>().OfType<GlobalExceptionHandler>().Single();

        using var responseBody = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            TraceIdentifier = traceIdentifier,
        };
        httpContext.Response.Body = responseBody;

        var handled = await handler.TryHandleAsync(httpContext, exception, TestContext.CurrentContext.CancellationToken);
        handled.Should().BeTrue();

        responseBody.Position = 0;
        var document = await JsonDocument.ParseAsync(responseBody);

        return (httpContext.Response.StatusCode, document.RootElement.Clone());
    }
}
