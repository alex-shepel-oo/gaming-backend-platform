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
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class LoginEndpointTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonWebTokenHandler TokenHandler = new();

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokenPairAndCreatesFamily()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, game.Slug, user.Email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();

        var familyCount = await CountFamiliesAsync(user.Id);
        familyCount.Should().Be(1);
    }

    [Fact]
    public async Task Login_UnconfirmedUser_Returns403EmailNotConfirmed()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: false, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, game.Slug, user.Email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("type").GetString().Should().Be("https://gaming-backend-platform/problems/email-not-confirmed");
    }

    [Fact]
    public async Task Login_AfterConfirmation_Succeeds()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: false, active: true);
        using var client = factory.CreateClient();

        var blocked = await LoginAsync(client, game.Slug, user.Email, Password);
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await ConfirmUserAsync(user.Id);

        var response = await LoginAsync(client, game.Slug, user.Email, Password);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, game.Slug, user.Email, "wrong-password-entirely");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_ReturnSameBody()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var wrongPassword = await LoginAsync(client, game.Slug, user.Email, "wrong-password-entirely");
        var unknownEmail = await LoginAsync(client, game.Slug, $"{Guid.NewGuid():N}@example.com", "wrong-password-entirely");

        wrongPassword.StatusCode.Should().Be(unknownEmail.StatusCode);

        var wrongPasswordBody = await NormalizedProblemBodyAsync(wrongPassword);
        var unknownEmailBody = await NormalizedProblemBodyAsync(unknownEmail);
        wrongPasswordBody.Should().Be(unknownEmailBody);
    }

    [Fact]
    public async Task Login_DisabledAccount_Returns403AccountDisabled()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: false);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, game.Slug, user.Email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Login_NoRoleInGame_Returns403NoAccessToGame()
    {
        var gameWithRole = await SeedGameAsync();
        var otherGame = await SeedGameAsync();
        var user = await SeedUserAsync(gameWithRole.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, otherGame.Slug, user.Email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Login_UnknownGameSlug_Returns404()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, $"no-such-game-{Guid.NewGuid():N}", user.Email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Login_PlatformLoginWithPlatformRole_Succeeds()
    {
        var user = await SeedUserAsync(gameId: null, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, gameSlug: null, user.Email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_NoGameSlugAndNoPlatformRole_ReturnsAccountScopedSession()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, gameSlug: null, user.Email, Password);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jwt = await DecodeAccessTokenAsync(response);
        jwt.GetClaim(IdentityClaims.Scope).Value.Should().Be(nameof(TokenScope.Account));
        jwt.TryGetClaim(IdentityClaims.Role, out _).Should().BeFalse();
        jwt.TryGetClaim(IdentityClaims.GameId, out _).Should().BeFalse();
        jwt.Claims.Where(c => c.Type == IdentityClaims.Perms).Select(c => c.Value).Should()
            .BeEquivalentTo([AccountPermissions.GamesList, AccountPermissions.ProfileManage]);
    }

    [Fact]
    public async Task Login_AccountScopedSession_RefreshKeepsAccountScopeAndPermissions()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var login = await LoginAsync(client, gameSlug: null, user.Email, Password);
        var loginBody = await login.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);

        var refresh = await client.PostAsJsonAsync(
            "/api/identity/auth/refresh",
            new RefreshRequest(loginBody!.RefreshToken),
            JsonOptions,
            TestContext.Current.CancellationToken);

        refresh.StatusCode.Should().Be(HttpStatusCode.OK);

        var jwt = await DecodeAccessTokenAsync(refresh);
        jwt.GetClaim(IdentityClaims.Scope).Value.Should().Be(nameof(TokenScope.Account));
        jwt.TryGetClaim(IdentityClaims.Role, out _).Should().BeFalse();
        jwt.Claims.Where(c => c.Type == IdentityClaims.Perms).Select(c => c.Value).Should()
            .BeEquivalentTo([AccountPermissions.GamesList, AccountPermissions.ProfileManage]);
    }

    [Fact]
    public async Task Login_PlatformAdmin_TokenCarriesPlatformScopeAndResolvedPermissions()
    {
        var user = await SeedUserAsync(gameId: null, confirmed: true, active: true, role: PlatformRole.Admin);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameId: null, Permissions.PlatformGamesManage, Permissions.GameBalanceAdjust);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, gameSlug: null, user.Email, Password);
        var jwt = await DecodeAccessTokenAsync(response);

        jwt.GetClaim(IdentityClaims.Scope).Value.Should().Be(nameof(TokenScope.Platform));
        jwt.Audiences.Should().ContainSingle().Which.Should().Be(TokenAudiences.Player);
        jwt.Claims.Where(c => c.Type == IdentityClaims.Perms).Select(c => c.Value).Should()
            .BeEquivalentTo([Permissions.PlatformGamesManage, Permissions.GameBalanceAdjust]);
    }

    [Fact]
    public async Task Login_Player_TokenCarriesGameScopeAndEmptyPermissions()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, game.Slug, user.Email, Password);
        var jwt = await DecodeAccessTokenAsync(response);

        jwt.GetClaim(IdentityClaims.Scope).Value.Should().Be(nameof(TokenScope.Game));
        jwt.GetClaim(IdentityClaims.GameId).Value.Should().Be(game.Id.ToString());
        jwt.Claims.Where(c => c.Type == IdentityClaims.Perms).Should().BeEmpty();
    }

    [Fact]
    public async Task NewlySeededUser_HasNullLastLoginAt_BeforeFirstLogin()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);

        var lastLoginAt = await LastLoginAtAsync(user.Id);

        lastLoginAt.Should().BeNull();
    }

    [Fact]
    public async Task Login_ValidCredentials_SetsLastLoginAt()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, game.Slug, user.Email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var lastLoginAt = await LastLoginAtAsync(user.Id);
        lastLoginAt.Should().BeCloseTo(factory.TimeProvider.GetUtcNow(), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Login_AccountScopedSession_SetsLastLoginAt()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, gameSlug: null, user.Email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var lastLoginAt = await LastLoginAtAsync(user.Id);
        lastLoginAt.Should().BeCloseTo(factory.TimeProvider.GetUtcNow(), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task SelectGame_DoesNotChangeLastLoginAt()
    {
        var homeGame = await SeedGameAsync();
        var targetGame = await SeedGameAsync();
        var user = await SeedUserAsync(homeGame.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var login = await LoginAsync(client, gameSlug: null, user.Email, Password);
        var loginTokens = await login.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);
        var lastLoginAtAfterLogin = await LastLoginAtAsync(user.Id);
        lastLoginAtAfterLogin.Should().NotBeNull();

        factory.TimeProvider.Advance(TimeSpan.FromSeconds(2));

        var selectGame = await PostAuthorizedAsync(
            client, "/api/identity/auth/select-game", loginTokens!.AccessToken, new SelectGameRequest(targetGame.Id));

        selectGame.StatusCode.Should().Be(HttpStatusCode.OK);
        var lastLoginAtAfterSelectGame = await LastLoginAtAsync(user.Id);
        lastLoginAtAfterSelectGame.Should().Be(lastLoginAtAfterLogin);
    }

    [Fact]
    public async Task Refresh_DoesNotChangeLastLoginAt()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var login = await LoginAsync(client, game.Slug, user.Email, Password);
        var loginBody = await login.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);
        var lastLoginAtAfterLogin = await LastLoginAtAsync(user.Id);
        lastLoginAtAfterLogin.Should().NotBeNull();

        factory.TimeProvider.Advance(TimeSpan.FromSeconds(2));

        var refresh = await client.PostAsJsonAsync(
            "/api/identity/auth/refresh",
            new RefreshRequest(loginBody!.RefreshToken),
            JsonOptions,
            TestContext.Current.CancellationToken);

        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var lastLoginAtAfterRefresh = await LastLoginAtAsync(user.Id);
        lastLoginAtAfterRefresh.Should().Be(lastLoginAtAfterLogin);
    }

    private static Task<HttpResponseMessage> PostAuthorizedAsync<TBody>(
        HttpClient client, string url, string accessToken, TBody body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonWebToken> DecodeAccessTokenAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);

        return TokenHandler.ReadJsonWebToken(body!.AccessToken);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string? gameSlug, string email, string password) =>
        client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(gameSlug, email, password),
            JsonOptions,
            TestContext.Current.CancellationToken);

    private static async Task<string> NormalizedProblemBodyAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var properties = json.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}");

        return string.Join('&', properties);
    }

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

    private async Task<User> SeedUserAsync(Guid? gameId, bool confirmed, bool active, PlatformRole role = PlatformRole.Player)
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
            IsActive = active,
            EmailConfirmed = confirmed,
            EmailConfirmedAt = confirmed ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Users.Add(user);
        dbContext.UserGameRoles.Add(new UserGameRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            GameId = gameId,
            Role = role,
            GrantedAt = now,
        });

        await dbContext.SaveChangesAsync();

        return user;
    }

    private async Task SeedRolePermissionsAsync(PlatformRole role, Guid? gameId, params string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        dbContext.RolePermissions.AddRange(permissions.Select(permission => new RolePermission
        {
            Id = Guid.CreateVersion7(),
            Role = role,
            GameId = gameId,
            Permission = permission,
            GrantedAt = now,
        }));

        await dbContext.SaveChangesAsync();
    }

    private async Task ConfirmUserAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        var user = await dbContext.Users.SingleAsync(u => u.Id == userId);
        user.EmailConfirmed = true;
        user.EmailConfirmedAt = now;

        await dbContext.SaveChangesAsync();
    }

    private async Task<int> CountFamiliesAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.RefreshTokenFamilies.CountAsync(f => f.UserId == userId);
    }

    private async Task<DateTimeOffset?> LastLoginAtAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.Users.Where(u => u.Id == userId).Select(u => u.LastLoginAt).SingleAsync();
    }
}
