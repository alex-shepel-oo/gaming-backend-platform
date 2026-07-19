using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace EconomyService.Tests.Integration;

[TestFixture]
public sealed class CorrelationIdMiddlewareTests : IDisposable
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() =>
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

    [OneTimeTearDown]
    public void OneTimeTearDown() => Dispose();

    public void Dispose() => _factory.Dispose();

    [Test]
    public async Task Health_NoCorrelationIdHeader_RespondsWithGeneratedId()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

        response.Headers.TryGetValues("X-Correlation-Id", out var values).Should().BeTrue();
        values!.Single().Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Health_CorrelationIdHeaderProvided_EchoesSameId()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-Id", "client-supplied-id");

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.Headers.GetValues("X-Correlation-Id").Single().Should().Be("client-supplied-id");
    }
}
