using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ApiGateway.Tests;

public sealed class EconomyRoutingTests : IDisposable
{
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes-long";
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
                ["Jwt:Key"] = SigningKey,
                ["Cors:AllowedOrigins:0"] = "http://localhost:8080",
            }));
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
        // still fails downstream -- what this asserts is that Ocelot recognizes
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
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return TokenHandler.CreateToken(descriptor);
    }
}
