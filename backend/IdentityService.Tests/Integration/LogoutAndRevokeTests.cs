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
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class LogoutAndRevokeTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Logout_ValidSession_Returns204AndInvalidatesRefreshFamily()
    {
        var (client, tokens, _, _) = await LoginAsPlayerAsync();

        var response = await LogoutAsync(client, tokens.AccessToken, tokens.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshed = await RefreshAsync(client, tokens.RefreshToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RefreshTokenBelongsToAnotherUser_Returns403()
    {
        var (clientA, tokensA, _, _) = await LoginAsPlayerAsync();
        var (_, tokensB, _, _) = await LoginAsPlayerAsync();

        var response = await LogoutAsync(clientA, tokensA.AccessToken, tokensB.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Logout_RevokedFamilyPresentedAgainByAFreshlyIssuedToken_IsANoOp()
    {
        var (client, tokens, userId, gameId) = await LoginAsPlayerAsync();
        await LogoutAsync(client, tokens.AccessToken, tokens.RefreshToken);

        var freshLogin = await LoginAsAsync(client, userId, gameId);

        var secondLogout = await LogoutAsync(client, freshLogin.AccessToken, tokens.RefreshToken);

        secondLogout.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_SameAccessTokenPresentedAgain_Returns401NotFromTheHandler()
    {
        var (client, tokens, _, _) = await LoginAsPlayerAsync();
        await LogoutAsync(client, tokens.AccessToken, tokens.RefreshToken);

        var replay = await LogoutAsync(client, tokens.AccessToken, tokens.RefreshToken);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeSessions_Admin_RevokesAllFamiliesAndWritesNoDenyListEntry()
    {
        var (playerClient, playerTokens, userId, _) = await LoginAsPlayerAsync();
        var (adminClient, adminToken) = await LoginAsAdminAsync();

        var response = await PostAuthorizedAsync(
            adminClient, $"/api/identity/users/{userId}/revoke-sessions", adminToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshed = await RefreshAsync(playerClient, playerTokens.RefreshToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var denyListCount = await CountDeniedAccessTokensAsync(userId);
        denyListCount.Should().Be(0);
    }

    [Fact]
    public async Task RevokeSessions_ScopedToGame_LeavesOtherGamesFamilyValid()
    {
        var (userId, gameA, gameB) = await SeedUserWithTwoGamesAsync();
        var tokensA = await LoginAsAsync(userId, gameA);
        var tokensB = await LoginAsAsync(userId, gameB);
        var (adminClient, adminToken) = await LoginAsAdminAsync();

        var response = await PostAuthorizedAsync(
            adminClient, $"/api/identity/users/{userId}/revoke-sessions?gameId={gameA}", adminToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var untouchedClient = factory.CreateClient();
        var refreshedA = await RefreshAsync(untouchedClient, tokensA.RefreshToken);
        var refreshedB = await RefreshAsync(untouchedClient, tokensB.RefreshToken);

        refreshedA.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refreshedB.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeSessions_NonAdmin_Returns403()
    {
        var (playerClient, playerTokens, userId, _) = await LoginAsPlayerAsync();

        var response = await PostAuthorizedAsync(
            playerClient, $"/api/identity/users/{userId}/revoke-sessions", playerTokens.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevokeSessions_UnknownUser_Returns404()
    {
        var (adminClient, adminToken) = await LoginAsAdminAsync();

        var response = await PostAuthorizedAsync(
            adminClient, $"/api/identity/users/{Guid.NewGuid()}/revoke-sessions", adminToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeSessions_GameScopedAdminTargetsAnotherGame_Returns403()
    {
        var (playerId, gameA, _) = await SeedUserWithTwoGamesAsync();
        var (gameAdminId, gameAdminGameId) = await SeedUserAsync(PlatformRole.Admin);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameAdminGameId, Permissions.GameRolesManage);
        var client = factory.CreateClient();
        var adminTokens = await LoginAsAsync(client, gameAdminId, gameAdminGameId);

        var response = await PostAuthorizedAsync(
            client, $"/api/identity/users/{playerId}/revoke-sessions?gameId={gameA}", adminTokens.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> LogoutAsync(HttpClient client, string accessToken, string refreshToken) =>
        PostAuthorizedAsync(
            client,
            "/api/identity/auth/logout",
            accessToken,
            JsonContent.Create(new LogoutRequest(refreshToken), options: JsonOptions));

    private static async Task<HttpResponseMessage> PostAuthorizedAsync(
        HttpClient client, string url, string accessToken, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync(
            "/api/identity/auth/refresh", new RefreshRequest(refreshToken), JsonOptions, TestContext.Current.CancellationToken);

    private async Task<(HttpClient Client, TokenPairResponse Tokens, Guid UserId, Guid GameId)> LoginAsPlayerAsync()
    {
        var (userId, gameId) = await SeedPlayerAsync();
        var client = factory.CreateClient();
        var tokens = await LoginAsAsync(client, userId, gameId);

        return (client, tokens, userId, gameId);
    }

    private Task<TokenPairResponse> LoginAsAsync(Guid userId, Guid? gameId) =>
        LoginAsAsync(factory.CreateClient(), userId, gameId);

    private async Task<TokenPairResponse> LoginAsAsync(HttpClient client, Guid userId, Guid? gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await dbContext.Users.SingleAsync(u => u.Id == userId);
        var gameSlug = gameId is null ? null : (await dbContext.Games.SingleAsync(g => g.Id == gameId)).Slug;

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(gameSlug, user.Email, Password),
            JsonOptions,
            TestContext.Current.CancellationToken);

        return (await response.Content.ReadFromJsonAsync<TokenPairResponse>(
            JsonOptions, TestContext.Current.CancellationToken))!;
    }

    private async Task<(HttpClient Client, string AccessToken)> LoginAsAdminAsync()
    {
        var adminId = await SeedPlatformAdminAsync();
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameId: null, Permissions.PlatformRolesManage);
        var client = factory.CreateClient();
        var tokens = await LoginAsAsync(client, adminId, gameId: null);

        return (client, tokens.AccessToken);
    }

    private Task<(Guid UserId, Guid GameId)> SeedPlayerAsync() => SeedUserAsync(PlatformRole.Player);

    private async Task<(Guid UserId, Guid GameId)> SeedUserAsync(PlatformRole role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = factory.TimeProvider.GetUtcNow();

        var game = new Game
        {
            Id = Guid.CreateVersion7(),
            Slug = $"game-{Guid.NewGuid():N}",
            Name = "Test Game",
            IsActive = true,
            CreatedAt = now,
        };

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = "Test User",
            PasswordHash = passwordHasher.Hash(Password),
            IsActive = true,
            EmailConfirmed = true,
            EmailConfirmedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Games.Add(game);
        dbContext.Users.Add(user);
        dbContext.UserGameRoles.Add(new UserGameRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            GameId = game.Id,
            Role = role,
            GrantedAt = now,
        });

        await dbContext.SaveChangesAsync();

        return (user.Id, game.Id);
    }

    private async Task<(Guid UserId, Guid GameA, Guid GameB)> SeedUserWithTwoGamesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = factory.TimeProvider.GetUtcNow();

        var gameA = new Game
        {
            Id = Guid.CreateVersion7(), Slug = $"game-{Guid.NewGuid():N}", Name = "Game A", IsActive = true, CreatedAt = now,
        };
        var gameB = new Game
        {
            Id = Guid.CreateVersion7(), Slug = $"game-{Guid.NewGuid():N}", Name = "Game B", IsActive = true, CreatedAt = now,
        };

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

        dbContext.Games.AddRange(gameA, gameB);
        dbContext.Users.Add(user);
        dbContext.UserGameRoles.AddRange(
            new UserGameRole { Id = Guid.CreateVersion7(), UserId = user.Id, GameId = gameA.Id, Role = PlatformRole.Player, GrantedAt = now },
            new UserGameRole { Id = Guid.CreateVersion7(), UserId = user.Id, GameId = gameB.Id, Role = PlatformRole.Player, GrantedAt = now });

        await dbContext.SaveChangesAsync();

        return (user.Id, gameA.Id, gameB.Id);
    }

    private async Task<int> CountDeniedAccessTokensAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.RevokedAccessTokens.CountAsync(t => t.UserId == userId);
    }

    private async Task<Guid> SeedPlatformAdminAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = factory.TimeProvider.GetUtcNow();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = "Test Platform Admin",
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
            GameId = null,
            Role = PlatformRole.Admin,
            GrantedAt = now,
        });

        await dbContext.SaveChangesAsync();

        return user.Id;
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
}
