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

public sealed class IdentityRoutingTests : IDisposable
{
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "gaming-backend-platform/identity";
    private const string Audience = "gbp-player";

    private static readonly JsonWebTokenHandler TokenHandler = new();

    // Development, not Testing: ocelot.Development.json resolves routes through
    // Consul service discovery, the only shape that passes Ocelot's own startup
    // validation without DownstreamHostAndPorts configured directly - see the
    // same note on CorsPolicyTests/EconomyRoutingTests.
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
    public async Task Get_UserRoles_UnderAdminPrefix_IsMatchedByNewAdminRoute()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/identity/users/{Guid.NewGuid()}/roles");
        request.Headers.Authorization = new("Bearer", IssueAccessToken(scope: null, audience: "gbp-admin"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // No live IdentityService in this test process, so this still fails
        // downstream - what this asserts is that the new
        // /api/admin/identity/users/{userId}/roles route (GET, PATCH) exists
        // and an aud=gbp-admin token clears the gateway's own claims check.
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_UserRoles_UnderOldPlayerFacingPrefix_IsGoneEntirely()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/identity/users/{Guid.NewGuid()}/roles");
        request.Headers.Authorization = new("Bearer", IssueAccessToken(scope: null));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // The old /api/identity/users/{everything} wildcard used to match
        // this path. It has been narrowed down to users/me only, so this
        // path no longer resolves to any route at all.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_UsersMe_UnderPlayerFacingPrefix_StillResolves()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/users/me");
        request.Headers.Authorization = new("Bearer", IssueAccessToken(scope: null));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_UsersList_UnderPlayerFacingPrefix_IsGone()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/users");
        request.Headers.Authorization = new("Bearer", IssueAccessToken(scope: null));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/api/identity/games")]
    [InlineData("/api/identity/permissions")]
    [InlineData("/api/identity/roles/Admin/permissions")]
    public async Task PlayerFacingAdminOnlyRoutes_AreGoneFromPlayerPrefix(string path)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", IssueAccessToken(scope: null));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_Games_WithGameScopeToken_IsRejectedAtGatewayBeforeReachingService()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/identity/games");
        request.Headers.Authorization = new("Bearer", IssueAccessToken(scope: "Game", audience: "gbp-admin"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_RolePermissions_WithGameScopeToken_ReachesServiceInsteadOfBeingBlockedAtGateway()
    {
        using var client = _factory.CreateClient();
        var gameId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/identity/roles/Admin/permissions?gameId={gameId}");
        request.Headers.Authorization = new("Bearer", IssueAccessToken(scope: "Game", audience: "gbp-admin"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // No live IdentityService in this test process, so this still fails
        // downstream. What matters is that it is NOT rejected at the gateway
        // (401/403) the way the games route above is - a scope=Game token is
        // exactly how a Game-Admin manages their own game's role_permissions,
        // and the gateway must let it through for the service's own
        // RoleEscalationGuard to make the real decision.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_AdminGames_WithAdminAudienceToken_IsNotBlockedAtGateway()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/identity/games");
        request.Headers.Authorization = new("Bearer", IssueAccessToken(scope: "Platform", audience: "gbp-admin"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_AdminGames_WithPlayerAudienceToken_IsRejectedAtGateway()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/identity/games");
        request.Headers.Authorization = new("Bearer", IssueAccessToken(scope: "Platform", audience: "gbp-player"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static string IssueAccessToken(string? scope, string audience = Audience)
    {
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = Guid.NewGuid().ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
        };

        if (scope is not null)
        {
            claims["scope"] = scope;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return TokenHandler.CreateToken(descriptor);
    }
}
