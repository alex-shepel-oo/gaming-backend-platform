using BuildingBlocks.Messaging;
using BuildingBlocks.Testing;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace NotificationService.Tests.Integration.Fixtures;

// Program.cs registers IRabbitMqConnection unconditionally and the /health/ready
// check resolves it, so every integration test class that boots the host needs a
// reachable broker just to start. One shared container for the whole Integration
// namespace avoids paying that startup cost per test class.
public sealed class RabbitMqFixture : IAsyncLifetime
{
    public RabbitMqContainer Container { get; } = new RabbitMqBuilder(TestContainerImages.RabbitMq)
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await Container.StartAsync();

        // BalanceChangedConsumer binds its queue to this exchange as soon as
        // the host starts, and RabbitMQ refuses a bind against an exchange
        // that doesn't exist yet. In production EconomyService always
        // declares it first; here nothing plays that role, so the fixture
        // has to declare it itself before any test builds the host.
        var options = MsOptions.Create(new RabbitMqOptions
        {
            Host = Container.Hostname,
            Port = Container.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
        });

        await using var connection = new RabbitMqConnection(options);
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(options.Value.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
    }

    public ValueTask DisposeAsync() => new(Container.DisposeAsync().AsTask());
}

[CollectionDefinition(nameof(NotificationApiCollectionDefinition))]
public sealed class NotificationApiCollectionDefinition : ICollectionFixture<RabbitMqFixture>;
