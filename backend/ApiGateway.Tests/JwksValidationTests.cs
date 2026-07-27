using System.Net;
using ApiGateway.Auth;
using ApiGateway.Tests.Fixtures;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ApiGateway.Tests;

// Session 2 of Group A9: the gateway no longer trusts a shared HS256 secret, it resolves
// Identity's published RSA key through JwksKeyCache instead. These tests exercise that
// resolver end to end (real JwksKeyCache, real background refresher, only the HTTP call at
// the bottom replaced with FakeJwksHandler) rather than unit-testing the cache in isolation.
public sealed class JwksValidationTests : IDisposable
{
    private const string Issuer = "gaming-backend-platform/identity";
    private const string Audience = "gbp-player";

    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly FakeJwksHandler _jwksHandler = new(TestJwks.JwksJson);

    // Development, not Testing: same reasoning as CorsPolicyTests/EconomyRoutingTests --
    // ocelot.Development.json resolves routes through Consul service discovery, the only
    // shape that passes Ocelot's own startup validation without a live Consul to query.
    private readonly WebApplicationFactory<Program> _factory;

    public JwksValidationTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:JwksUri"] = "https://identity.test/.well-known/jwks.json",
                    ["Cors:AllowedOrigins:0"] = "http://localhost:8080",
                }));

            builder.ConfigureServices(services => services
                .AddHttpClient<IJwksKeyCache, JwksKeyCache>()
                .ConfigurePrimaryHttpMessageHandler(() => _jwksHandler));
        });
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Request_TokenSignedWithIdentitysRealRs256Key_IsNotRejectedAtTheGateway()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/economy/currencies");
        request.Headers.Authorization = new("Bearer", IssueAccessToken());

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_TokenSignedByTreatingThePublicKeyAsAnHmacSecret_IsRejected()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/economy/currencies");
        request.Headers.Authorization = new("Bearer", IssueTokenSignedAsHmacConfusionAttempt());

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cache_SeveralRequestsInARow_FetchesJwksOnlyOnceForTheWholeFactoryLifetime()
    {
        var token = IssueAccessToken();
        using var client = _factory.CreateClient();

        // The first request is what actually boots the host (and so triggers Program.cs's
        // own one-time blocking refresh) -- send it first so that startup fetch is already
        // accounted for, then take the baseline.
        using (var warmupRequest = new HttpRequestMessage(HttpMethod.Get, "/api/economy/currencies"))
        {
            warmupRequest.Headers.Authorization = new("Bearer", token);
            await client.SendAsync(warmupRequest, TestContext.Current.CancellationToken);
        }

        var requestCountAfterWarmup = _jwksHandler.RequestCount;

        for (var i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/economy/currencies");
            request.Headers.Authorization = new("Bearer", token);
            await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        // The whole point of the background-refreshed cache: five more validated requests
        // must not cause five more JWKS fetches, or any at all -- the resolver only ever
        // reads the already-warm in-memory snapshot.
        _jwksHandler.RequestCount.Should().Be(requestCountAfterWarmup);
    }

    [Fact]
    public async Task Cache_RefreshFailsAfterAGoodFetch_StillValidatesAgainstTheLastKnownGoodSnapshot()
    {
        var token = IssueAccessToken();
        var jwksKeyCache = _factory.Services.GetRequiredService<IJwksKeyCache>();

        // The factory's startup refresh already succeeded once (or this call succeeds now if
        // it hadn't yet) -- either way, there is a good snapshot cached before the endpoint is
        // simulated as unreachable.
        await jwksKeyCache.RefreshAsync(TestContext.Current.CancellationToken);

        _jwksHandler.ShouldFail = true;
        try
        {
            await jwksKeyCache.RefreshAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            _jwksHandler.ShouldFail = false;
        }

        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/economy/currencies");
        request.Headers.Authorization = new("Bearer", token);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
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

    // The classic RS256-to-HS256 downgrade: the attacker has only ever seen the RSA public key
    // (from the legitimate JWKS response) and signs a token by treating those public key bytes
    // as if they were a shared HMAC secret, hoping a validator that resolves a key by kid alone
    // -- without checking which algorithm actually signed the token -- accepts it.
    private static string IssueTokenSignedAsHmacConfusionAttempt()
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
                new SymmetricSecurityKey(TestJwks.PublicKeyBytesForHmacConfusionAttempt()) { KeyId = TestJwks.SigningKey.KeyId },
                SecurityAlgorithms.HmacSha256),
        };

        return TokenHandler.CreateToken(descriptor);
    }
}
