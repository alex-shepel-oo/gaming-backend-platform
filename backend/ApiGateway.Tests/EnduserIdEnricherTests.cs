using System.Diagnostics;
using System.Security.Claims;
using ApiGateway.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace ApiGateway.Tests;

public sealed class EnduserIdEnricherTests
{
    [Fact]
    public async Task ApplyAsync_AuthenticatedRequest_TagsActivityAndPushesLogContextProperty()
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

        // Emitted from inside next(), the same place a real request's log lines would be written -
        // the property has to already be pushed by the time this runs.
        Func<Task> next = () =>
        {
            logger.Information("marker");
            return Task.CompletedTask;
        };

        await EnduserIdEnricher.ApplyAsync(context, next);

        activity.TagObjects.Should().ContainSingle(t => t.Key == "enduser.id" && Equals(t.Value, userId));

        events.Should().ContainSingle();
        events[0].Properties.Should().ContainKey("enduser.id");
        events[0].Properties["enduser.id"].ToString().Should().Contain(userId);
    }

    [Fact]
    public async Task ApplyAsync_UnauthenticatedRequest_CallsNextWithoutTaggingOrPushingLogContext()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity()),
        };

        using var activity = new Activity("test-request").Start();

        var nextCalled = false;
        Func<Task> next = () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        await EnduserIdEnricher.ApplyAsync(context, next);

        nextCalled.Should().BeTrue();
        activity.TagObjects.Should().NotContain(t => t.Key == "enduser.id");
    }

    private sealed class DelegateSink(Action<LogEvent> onEmit) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => onEmit(logEvent);
    }
}
