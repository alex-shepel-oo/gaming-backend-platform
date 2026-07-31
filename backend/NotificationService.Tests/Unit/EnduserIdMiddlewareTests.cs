using System.Diagnostics;
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using NotificationService.Infrastructure;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace NotificationService.Tests.Unit;

public sealed class EnduserIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AuthenticatedRequest_TagsActivityAndPushesLogContextProperty()
    {
        var userId = Guid.NewGuid().ToString();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtRegisteredClaimNames.Sub, userId)], authenticationType: "TestAuth")),
        };

        using var activity = new Activity("test-request").Start();

        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new DelegateSink(events.Add))
            .CreateLogger();

        RequestDelegate next = _ =>
        {
            // Emitted from inside next(), the same place a real request's log lines would be
            // written - the property has to already be pushed by the time this runs.
            logger.Information("marker");
            return Task.CompletedTask;
        };

        var middleware = new EnduserIdMiddleware(next);
        await middleware.InvokeAsync(context);

        activity.TagObjects.Should().ContainSingle(t => t.Key == "enduser.id" && Equals(t.Value, userId));

        events.Should().ContainSingle();
        events[0].Properties.Should().ContainKey("enduser.id");
        events[0].Properties["enduser.id"].ToString().Should().Contain(userId);
    }

    [Fact]
    public async Task InvokeAsync_UnauthenticatedRequest_CallsNextWithoutTaggingOrPushingLogContext()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity()),
        };

        using var activity = new Activity("test-request").Start();

        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new EnduserIdMiddleware(next);
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        activity.TagObjects.Should().NotContain(t => t.Key == "enduser.id");
    }

    private sealed class DelegateSink(Action<LogEvent> onEmit) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => onEmit(logEvent);
    }
}
