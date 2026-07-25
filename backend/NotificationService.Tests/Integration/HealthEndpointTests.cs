using System.Net;
using AwesomeAssertions;
using NotificationService.Tests.Integration.Fixtures;
using Xunit;

namespace NotificationService.Tests.Integration;

[Collection(nameof(NotificationApiCollectionDefinition))]
public sealed class HealthEndpointTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task Health_Returns200()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_RabbitMqReachable_Returns200()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
