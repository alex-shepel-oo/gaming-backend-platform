using BuildingBlocks.Testing;
using Testcontainers.RabbitMq;
using Xunit;

namespace IdentityService.Tests.Integration.Fixtures;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder(TestContainerImages.RabbitMq)
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public string Hostname => _container.Hostname;
    public ushort Port => _container.GetMappedPublicPort(5672);

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
