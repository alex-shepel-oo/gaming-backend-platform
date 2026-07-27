using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using IdentityService.Auth;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Persistence;
using IdentityService.Services;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class JwksEndpointTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly string[] PrivateKeyFieldNames = ["d", "p", "q", "dp", "dq", "qi"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonWebTokenHandler TokenHandler = new();

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Login_IssuedToken_IsSignedRs256WithAKidThatMatchesTheJwks()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id);
        using var client = factory.CreateClient();

        var login = await LoginAsync(client, game.Slug, user.Email);
        var jwt = await DecodeAccessTokenAsync(login);

        jwt.Alg.Should().Be(SecurityAlgorithms.RsaSha256);
        jwt.Kid.Should().NotBeNullOrEmpty();

        var jwks = await GetJwksAsync(client);
        jwks.RootElement.GetProperty("keys").EnumerateArray().Single()
            .GetProperty("kid").GetString().Should().Be(jwt.Kid);
    }

    [Fact]
    public async Task Jwks_RequiresNoAuthorizationHeader_AndReturnsExactlyOneKey()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/jwks.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("keys").EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task Jwks_ContainsNoPrivateKeyFields()
    {
        using var client = factory.CreateClient();

        using var jwks = await GetJwksAsync(client);

        AssertNoPrivateKeyFields(jwks.RootElement);
    }

    [Fact]
    public async Task FreshlyIssuedToken_StillAuthorizesIdentitysOwnEndpoints()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id);
        using var client = factory.CreateClient();

        var login = await LoginAsync(client, game.Slug, user.Email);
        var body = await login.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForgedTokenWithAlgNone_IsRejected()
    {
        var forgedToken = TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "gaming-backend-platform/identity",
            Audience = TokenAudiences.Player,
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = Guid.NewGuid().ToString() },
            Expires = DateTime.UtcNow.AddMinutes(15),
        });

        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static void AssertNoPrivateKeyFields(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    PrivateKeyFieldNames.Should().NotContain(property.Name);
                    AssertNoPrivateKeyFields(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoPrivateKeyFields(item);
                }

                break;
        }
    }

    private static async Task<JsonDocument> GetJwksAsync(HttpClient client)
    {
        var response = await client.GetAsync("/.well-known/jwks.json", TestContext.Current.CancellationToken);

        return await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("JWKS response body was empty.");
    }

    private static async Task<JsonWebToken> DecodeAccessTokenAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);

        return TokenHandler.ReadJsonWebToken(body!.AccessToken);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string gameSlug, string email) =>
        client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(gameSlug, email, Password),
            JsonOptions,
            TestContext.Current.CancellationToken);

    private async Task<Game> SeedGameAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        var game = new Game
        {
            Id = Guid.CreateVersion7(),
            Slug = $"game-{Guid.NewGuid():N}",
            Name = "Test Game",
            IsActive = true,
            CreatedAt = now,
        };

        dbContext.Games.Add(game);
        await dbContext.SaveChangesAsync();

        return game;
    }

    private async Task<User> SeedUserAsync(Guid gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = factory.TimeProvider.GetUtcNow();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = "Test Player",
            PasswordHash = passwordHasher.Hash(Password),
            IsActive = true,
            EmailConfirmed = true,
            EmailConfirmedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Users.Add(user);

        dbContext.UserGameRoles.Add(new UserGameRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            GameId = gameId,
            Role = PlatformRole.Player,
            GrantedAt = now,
        });

        await dbContext.SaveChangesAsync();

        return user;
    }
}
