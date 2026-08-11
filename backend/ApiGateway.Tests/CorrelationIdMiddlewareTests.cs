using ApiGateway.Tests.Fixtures;
using AwesomeAssertions;
using BuildingBlocks.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApiGateway.Tests;

public sealed class CorrelationIdMiddlewareTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        // Same reasoning as CorsPolicyTests: Development resolves ocelot.Development.json's
        // Consul-based routing, the only shape that passes Ocelot's startup validation
        // without a live Consul to query. These tests only ever hit /health.
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:JwksUri"] = "https://identity.test/.well-known/jwks.json",
            }));

        builder.ConfigureServices(services => services
            .AddHttpClient<IJwksKeyCache, JwksKeyCache>()
            .ConfigurePrimaryHttpMessageHandler(() => new FakeJwksHandler(TestJwks.JwksJson)));
    });

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Health_NoCorrelationIdHeader_RespondsWithGeneratedId()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.TryGetValues("X-Correlation-Id", out var values).Should().BeTrue();
        values!.Single().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Health_CorrelationIdHeaderProvided_EchoesSameId()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-Id", "client-supplied-id");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Correlation-Id").Single().Should().Be("client-supplied-id");
    }
}
