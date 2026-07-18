using System.Text.Json;
using AwesomeAssertions;
using IdentityService.Exceptions;
using IdentityService.Extensions;
using IdentityService.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Unit;

public class GlobalExceptionHandlerTests
{
    [Theory]
    [InlineData(typeof(InvalidCredentialsException), StatusCodes.Status401Unauthorized)]
    [InlineData(typeof(InvalidRefreshTokenException), StatusCodes.Status401Unauthorized)]
    [InlineData(typeof(GameNotFoundException), StatusCodes.Status404NotFound)]
    [InlineData(typeof(EmailAlreadyExistsException), StatusCodes.Status409Conflict)]
    [InlineData(typeof(InvalidVerificationCodeException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(AccountDisabledException), StatusCodes.Status403Forbidden)]
    [InlineData(typeof(EmailNotConfirmedException), StatusCodes.Status403Forbidden)]
    [InlineData(typeof(NoAccessToGameException), StatusCodes.Status403Forbidden)]
    public async Task TryHandleAsync_DomainException_WritesMatchingStatusAndDetail(Type exceptionType, int expectedStatus)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        var (statusCode, body) = await HandleAsync(exception);

        statusCode.Should().Be(expectedStatus);
        body.GetProperty("status").GetInt32().Should().Be(expectedStatus);
        body.GetProperty("detail").GetString().Should().Be(exception.Message);
    }

    [Fact]
    public async Task TryHandleAsync_UnknownException_Returns500WithoutLeakingExceptionMessage()
    {
        var exception = new InvalidOperationException("connection string contains a password");

        var (statusCode, body) = await HandleAsync(exception);

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
        body.GetProperty("detail").GetString().Should().NotContain("password");
    }

    [Fact]
    public async Task TryHandleAsync_EmailNotConfirmed_SetsDistinguishableType()
    {
        var (_, body) = await HandleAsync(new EmailNotConfirmedException());

        body.GetProperty("type").GetString().Should().Be("https://gaming-backend-platform/problems/email-not-confirmed");
    }

    [Fact]
    public async Task TryHandleAsync_TraceIdMatchesRequestTraceIdentifier()
    {
        var (_, body) = await HandleAsync(new InvalidCredentialsException(), traceIdentifier: "test-correlation-id");

        body.GetProperty("traceId").GetString().Should().Be("test-correlation-id");
    }

    private static async Task<(int StatusCode, JsonElement Body)> HandleAsync(
        Exception exception, string traceIdentifier = "trace-id")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityExceptionHandling();
        await using var provider = services.BuildServiceProvider();

        var handler = provider.GetServices<IExceptionHandler>().OfType<GlobalExceptionHandler>().Single();

        using var responseBody = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            TraceIdentifier = traceIdentifier,
        };
        httpContext.Response.Body = responseBody;

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);
        handled.Should().BeTrue();

        responseBody.Position = 0;
        var document = await JsonDocument.ParseAsync(responseBody);

        return (httpContext.Response.StatusCode, document.RootElement.Clone());
    }
}
