using BuildingBlocks.Messaging;
using BuildingBlocks.Testing;
using NUnit.Framework;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace EconomyService.Tests.Integration;

// Program.cs declares RabbitMQ topology as a hosted service that fails fast
// on startup if the broker is unreachable - the app has a real, hard
// dependency on RabbitMQ from this point on. Every integration test
// class that boots the host - not just the ones testing messaging - needs a
// reachable broker just to start. One shared container for the whole
// Integration namespace avoids paying that startup cost per test class.
[SetUpFixture]
public sealed class RabbitMqTestBroker
{
    // Same identity-owned exchange UserEmailConfirmedConsumer binds to at
    // startup. EconomyApiFactory only ever declares gbp.economy (its own
    // publish-side exchange); nothing in this test suite plays the role of
    // identity-service actually declaring gbp.identity. Without it, every
    // EconomyApiFactory-backed test class - not just the welcome-grant ones -
    // fails its background consumer's queue bind at host startup, which
    // brings the whole host down, mirrored here from the test side.
    private const string IdentityExchangeName = "gbp.identity";

    public static RabbitMqContainer Container { get; } = new RabbitMqBuilder(TestContainerImages.RabbitMq)
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        await Container.StartAsync();

        var options = new RabbitMqOptions
        {
            Host = Container.Hostname,
            Port = Container.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
        };

        await using var connection = new RabbitMqConnection(MsOptions.Create(options));
        await using var channel = await connection.CreateChannelAsync(TestContext.CurrentContext.CancellationToken);
        await channel.ExchangeDeclareAsync(
            IdentityExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: TestContext.CurrentContext.CancellationToken);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await Container.DisposeAsync();
}
