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
public sealed class SelectGameEndpointTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonWebTokenHandler TokenHandler = new();

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SelectGame_AccountScopedToken_NoExistingRole_SelfJoinsAsPlayerAndReturnsGameScopedToken()
    {
        var homeGame = await SeedGameAsync();
        var targetGame = await SeedGameAsync();
        await SeedRolePermissionsAsync(PlatformRole.Player, targetGame.Id, Permissions.GameBalanceAdjust);
        var user = await SeedUserAsync(homeGame.Id, confirmed: true, active: true);
        var (client, accountToken) = await LoginAccountScopedAsync(user.Email);

        var response = await PostAuthorizedAsync(
            client, "/api/identity/auth/select-game", accountToken, new SelectGameRequest(targetGame.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jwt = await DecodeAccessTokenAsync(response);
        jwt.GetClaim(IdentityClaims.Scope).Value.Should().Be(nameof(TokenScope.Game));
        jwt.GetClaim(IdentityClaims.GameId).Value.Should().Be(targetGame.Id.ToString());
        jwt.GetClaim(IdentityClaims.Role).Value.Should().Be(nameof(PlatformRole.Player));
        jwt.Claims.Where(c => c.Type == IdentityClaims.Perms).Select(c => c.Value).Should()
            .BeEquivalentTo([Permissions.GameBalanceAdjust]);

        var role = await RoleAsync(user.Id, targetGame.Id);
        role.Should().NotBeNull();
        role!.Role.Should().Be(PlatformRole.Player);

        var roleCount = await RoleCountAsync(user.Id, targetGame.Id);
        roleCount.Should().Be(1);
    }

    [Fact]
    public async Task SelectGame_CalledTwiceForSameGame_DoesNotDuplicateTheRole()
    {
        var homeGame = await SeedGameAsync();
        var targetGame = await SeedGameAsync();
        var user = await SeedUserAsync(homeGame.Id, confirmed: true, active: true);
        var (client, accountToken) = await LoginAccountScopedAsync(user.Email);

        var first = await PostAuthorizedAsync(
            client, "/api/identity/auth/select-game", accountToken, new SelectGameRequest(targetGame.Id));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await PostAuthorizedAsync(
            client, "/api/identity/auth/select-game", accountToken, new SelectGameRequest(targetGame.Id));
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var roleCount = await RoleCountAsync(user.Id, targetGame.Id);
        roleCount.Should().Be(1);
    }

    [Fact]
    public async Task SelectGame_UnknownGameId_Returns404()
    {
        var homeGame = await SeedGameAsync();
        var user = await SeedUserAsync(homeGame.Id, confirmed: true, active: true);
        var (client, accountToken) = await LoginAccountScopedAsync(user.Email);

        var response = await PostAuthorizedAsync(
            client, "/api/identity/auth/select-game", accountToken, new SelectGameRequest(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SelectGame_InactiveGame_Returns404()
    {
        var homeGame = await SeedGameAsync();
        var inactiveGame = await SeedGameAsync(isActive: false);
        var user = await SeedUserAsync(homeGame.Id, confirmed: true, active: true);
        var (client, accountToken) = await LoginAccountScopedAsync(user.Email);

        var response = await PostAuthorizedAsync(
            client, "/api/identity/auth/select-game", accountToken, new SelectGameRequest(inactiveGame.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SelectGame_FromGameScopedToken_SwitchesToDifferentGame()
    {
        var firstGame = await SeedGameAsync();
        var secondGame = await SeedGameAsync();
        var user = await SeedUserAsync(firstGame.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(firstGame.Slug, user.Email, Password),
            JsonOptions,
            TestContext.Current.CancellationToken);
        var loginTokens = await login.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);

        var response = await PostAuthorizedAsync(
            client, "/api/identity/auth/select-game", loginTokens!.AccessToken, new SelectGameRequest(secondGame.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jwt = await DecodeAccessTokenAsync(response);
        jwt.GetClaim(IdentityClaims.Scope).Value.Should().Be(nameof(TokenScope.Game));
        jwt.GetClaim(IdentityClaims.GameId).Value.Should().Be(secondGame.Id.ToString());

        var roleCount = await RoleCountAsync(user.Id, secondGame.Id);
        roleCount.Should().Be(1);
    }

    [Fact]
    public async Task Login_WithGameSlug_NoExistingRole_StillReturns403NoAccessToGame()
    {
        var homeGame = await SeedGameAsync();
        var otherGame = await SeedGameAsync();
        var user = await SeedUserAsync(homeGame.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(otherGame.Slug, user.Email, Password),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var roleCount = await RoleCountAsync(user.Id, otherGame.Id);
        roleCount.Should().Be(0);
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

    private async Task<(HttpClient Client, string AccessToken)> LoginAccountScopedAsync(string email)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(GameSlug: null, email, Password),
            JsonOptions,
            TestContext.Current.CancellationToken);

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);

        return (client, tokens!.AccessToken);
    }

    private async Task<Game> SeedGameAsync(bool isActive = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        var game = new Game
        {
            Id = Guid.CreateVersion7(),
            Slug = $"game-{Guid.NewGuid():N}",
            Name = "Test Game",
            IsActive = isActive,
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

    private async Task<UserGameRole?> RoleAsync(Guid userId, Guid gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.UserGameRoles.SingleOrDefaultAsync(r => r.UserId == userId && r.GameId == gameId);
    }

    private async Task<int> RoleCountAsync(Guid userId, Guid gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.UserGameRoles.CountAsync(r => r.UserId == userId && r.GameId == gameId);
    }
}
