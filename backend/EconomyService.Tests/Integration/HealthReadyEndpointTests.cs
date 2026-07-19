using System.Net;
using AwesomeAssertions;
using EconomyService.Tests.Integration.Fixtures;
using NUnit.Framework;

namespace EconomyService.Tests.Integration;

[TestFixture]
public sealed class HealthReadyEndpointTests : IAsyncDisposable
{
    private EconomyApiFactory _factory = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _factory = new EconomyApiFactory();
        await _factory.InitializeAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        _factory.Dispose();
        await _factory.StopContainerAsync();
    }

    [Test]
    public async Task HealthReady_DatabaseReachable_Returns200()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
