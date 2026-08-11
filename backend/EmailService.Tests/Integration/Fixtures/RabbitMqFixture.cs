using BuildingBlocks.Messaging;
using BuildingBlocks.Testing;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace EmailService.Tests.Integration.Fixtures;

// One shared container for the whole Integration namespace, same reasoning as
// NotificationService.Tests' own RabbitMqFixture.
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private const string ExchangeName = "gbp.identity";

    public RabbitMqContainer Container { get; } = new RabbitMqBuilder(TestContainerImages.RabbitMq)
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await Container.StartAsync();

        // Every EmailService consumer binds its queue to gbp.identity as soon as it starts, and
        // RabbitMQ refuses a bind against an exchange that doesn't exist yet. In production
        // identity-service's own topology initializer always declares it first; here nothing plays
        // that role, so the fixture declares it itself before any test builds a consumer.
        var options = MsOptions.Create(new RabbitMqOptions
        {
            Host = Container.Hostname,
            Port = Container.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
            ExchangeName = ExchangeName,
        });

        await using var connection = new RabbitMqConnection(options);
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
    }

    public ValueTask DisposeAsync() => new(Container.DisposeAsync().AsTask());
}

[CollectionDefinition(nameof(EmailServiceRabbitMqCollectionDefinition))]
public sealed class EmailServiceRabbitMqCollectionDefinition : ICollectionFixture<RabbitMqFixture>;
