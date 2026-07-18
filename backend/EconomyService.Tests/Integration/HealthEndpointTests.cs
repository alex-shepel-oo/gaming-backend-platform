using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace EconomyService.Tests.Integration;

[TestFixture]
public sealed class HealthEndpointTests : IDisposable
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => _factory = new WebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void OneTimeTearDown() => Dispose();

    public void Dispose() => _factory.Dispose();

    [Test]
    public async Task Health_Returns200()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
