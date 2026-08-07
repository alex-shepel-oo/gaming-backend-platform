using System.Net;
using ApiGateway.Tests.Fixtures;
using AwesomeAssertions;
using BuildingBlocks.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ApiGateway.Tests;

public sealed class EconomyRoutingTests : IDisposable
{
    private const string Issuer = "gaming-backend-platform/identity";
    private const string Audience = "gbp-player";

    private static readonly JsonWebTokenHandler TokenHandler = new();

    // Development, not Testing: ocelot.Development.json resolves routes through
    // Consul service discovery, the only shape that passes Ocelot's own startup
    // validation without DownstreamHostAndPorts configured directly - see the
    // same note on CorsPolicyTests.
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:JwksUri"] = "https://identity.test/.well-known/jwks.json",
                ["Cors:AllowedOrigins:0"] = "http://localhost:8080",
            }));

        // The app's startup path does one blocking JWKS refresh before it accepts any
        // requests; give it a real, matching key pair so the token this test issues
        // below actually resolves.
        builder.ConfigureServices(services => services
            .AddHttpClient<IJwksKeyCache, JwksKeyCache>()
            .ConfigurePrimaryHttpMessageHandler(() => new FakeJwksHandler(TestJwks.JwksJson)));
    });

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Get_EconomyCurrencies_IsMatchedByOcelot()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/economy/currencies");
        request.Headers.Authorization = new("Bearer", IssueAccessToken());

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // No live Consul/EconomyService in this test process, so the request
        // still fails downstream: what this asserts is that Ocelot recognizes
        // the route at all. A 404 here means ocelot.json has no matching
        // UpstreamPathTemplate; anything else means the route matched and the
        // failure happened trying to reach the (absent) downstream.
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    private static string IssueAccessToken()
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = Guid.NewGuid().ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(TestJwks.SigningKey, SecurityAlgorithms.RsaSha256),
        };

        return TokenHandler.CreateToken(descriptor);
    }
}
