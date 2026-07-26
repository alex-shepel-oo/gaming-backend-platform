using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ApiGateway.Tests;

public sealed class CorsPolicyTests : IDisposable
{
    private const string AllowedOrigin = "http://localhost:8080";
    private const string AllowedAdminOrigin = "http://localhost:8081";
    private const string DisallowedOrigin = "http://evil.example.com";

    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        // Development, not Testing: ocelot.Development.json already resolves routes through
        // Consul service discovery (ServiceName, no DownstreamHostAndPorts), which is the only
        // shape that passes Ocelot's own startup validation without a live Consul to query -
        // these tests never reach a route Ocelot would actually dispatch.
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes-long",
                ["Cors:AllowedOrigins:0"] = AllowedOrigin,
                ["AdminCors:AllowedOrigins:0"] = AllowedAdminOrigin,
            }));
    });

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Preflight_FromAllowedOrigin_ReturnsMatchingCorsHeaders()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "X-Client-Type");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues("Access-Control-Allow-Origin").Single().Should().Be(AllowedOrigin);
        response.Headers.GetValues("Access-Control-Allow-Credentials").Single().Should().Be("true");
        response.Headers.GetValues("Access-Control-Allow-Headers").Single().Should().Contain("X-Client-Type");
    }

    [Fact]
    public async Task Preflight_FromDisallowedOrigin_OmitsCorsHeaders()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", DisallowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Request_FromAllowedOrigin_EchoesOriginBackOnResponse()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues("Access-Control-Allow-Origin").Single().Should().Be(AllowedOrigin);
    }

    [Fact]
    public async Task Request_FromDisallowedOrigin_OmitsCorsHeaders()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", DisallowedOrigin);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_ToAdminPath_FromAdminOrigin_ReturnsMatchingCorsHeaders()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/admin/identity/games");
        request.Headers.Add("Origin", AllowedAdminOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "X-Client-Type");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues("Access-Control-Allow-Origin").Single().Should().Be(AllowedAdminOrigin);
        response.Headers.GetValues("Access-Control-Allow-Credentials").Single().Should().Be("true");
    }

    [Fact]
    public async Task Preflight_ToAdminPath_FromPlayerOrigin_OmitsCorsHeaders()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/admin/identity/games");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // The admin path is governed by AdminClientCors, not PlayerClientCors -
        // the player-client origin is not on the admin allow-list.
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Request_ToNonAdminPath_FromAdminOrigin_OmitsCorsHeaders()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AllowedAdminOrigin);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // /health is not under /api/admin, so it stays governed by
        // PlayerClientCors - the admin origin is not on that allow-list.
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
