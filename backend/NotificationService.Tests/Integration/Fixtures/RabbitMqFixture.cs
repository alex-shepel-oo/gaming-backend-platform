using Testcontainers.RabbitMq;
using Xunit;

namespace NotificationService.Tests.Integration.Fixtures;

// Program.cs registers IRabbitMqConnection unconditionally and the /health/ready
// check resolves it, so every integration test class that boots the host needs a
// reachable broker just to start. One shared container for the whole Integration
// namespace avoids paying that startup cost per test class.
public sealed class RabbitMqFixture : IAsyncLifetime
{
    public RabbitMqContainer Container { get; } = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public ValueTask InitializeAsync() => new(Container.StartAsync());

    public ValueTask DisposeAsync() => new(Container.DisposeAsync().AsTask());
}

[CollectionDefinition(nameof(NotificationApiCollectionDefinition))]
public sealed class NotificationApiCollectionDefinition : ICollectionFixture<RabbitMqFixture>;
