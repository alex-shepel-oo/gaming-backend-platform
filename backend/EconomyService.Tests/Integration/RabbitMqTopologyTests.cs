using System.Globalization;
using AwesomeAssertions;
using BuildingBlocks.Messaging.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace EconomyService.Tests.Integration;

[TestFixture]
public sealed class RabbitMqTopologyTests
{
    [Test]
    public async Task DeclareTopology_RunAgainAfterAnEarlierDeclare_DoesNotThrow()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        using (var firstHost = BuildHost())
        {
            await firstHost.StartAsync(cancellationToken);
            await firstHost.StopAsync(cancellationToken);
        }

        using var secondHost = BuildHost();
        var act = async () => await secondHost.StartAsync(cancellationToken);

        await act.Should().NotThrowAsync();

        await secondHost.StopAsync(cancellationToken);
    }

    private static IHost BuildHost()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:Host"] = RabbitMqTestBroker.Container.Hostname,
                ["RabbitMq:Port"] = RabbitMqTestBroker.Container.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
                ["RabbitMq:Username"] = "guest",
                ["RabbitMq:Password"] = "guest",
            })
            .Build();

        return Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddRabbitMqEventBus(configuration))
            .Build();
    }
}
