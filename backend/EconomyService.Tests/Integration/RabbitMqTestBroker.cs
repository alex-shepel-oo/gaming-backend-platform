using NUnit.Framework;
using Testcontainers.RabbitMq;

namespace EconomyService.Tests.Integration;

// Program.cs now declares RabbitMQ topology as a hosted service that fails
// fast on startup if the broker is unreachable (A.1: the app has a real,
// hard dependency on RabbitMQ from this point on). Every integration test
// class that boots the host - not just the ones testing messaging - needs a
// reachable broker just to start. One shared container for the whole
// Integration namespace avoids paying that startup cost per test class.
[SetUpFixture]
public sealed class RabbitMqTestBroker
{
    public static RabbitMqContainer Container { get; } = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync() => await Container.StartAsync();

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await Container.DisposeAsync();
}
