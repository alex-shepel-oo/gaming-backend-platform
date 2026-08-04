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
public sealed class AuthorizationPolicyTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetUserById_Anonymous_Returns401()
    {
        var game = await SeedGameAsync();
        var target = await SeedUserAsync(game.Id, PlatformRole.Player);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/identity/users/{target.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUserById_AsPlayer_Returns403()
    {
        var game = await SeedGameAsync();
        var player = await SeedUserAsync(game.Id, PlatformRole.Player);
        var (client, accessToken) = await LoginAsync(player.Id, game.Id);

        var response = await GetAuthorizedAsync(client, $"/api/identity/users/{player.Id}", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUserById_AsModerator_Returns200()
    {
        var game = await SeedGameAsync();
        var moderator = await SeedUserAsync(game.Id, PlatformRole.Moderator);
        await SeedRolePermissionsAsync(PlatformRole.Moderator, game.Id, Permissions.GamePlayersModerate);
        var (client, accessToken) = await LoginAsync(moderator.Id, game.Id);

        var response = await GetAuthorizedAsync(client, $"/api/identity/users/{moderator.Id}?gameId={game.Id}", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListGames_AsModerator_Returns403()
    {
        var game = await SeedGameAsync();
        var moderator = await SeedUserAsync(game.Id, PlatformRole.Moderator);
        var (client, accessToken) = await LoginAsync(moderator.Id, game.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/games", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListGames_AsGameScopedAdminWithoutPlatformGamesManage_Returns403()
    {
        // /games moved off the role-based Admin policy onto a platform.games.manage
        // permission check (see GameEndpointsTests) - a game-scoped Admin no longer
        // gets a free pass just by holding the Admin role in their own game.
        var game = await SeedGameAsync();
        var admin = await SeedUserAsync(game.Id, PlatformRole.Admin);
        var (client, accessToken) = await LoginAsync(admin.Id, game.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/games", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> GetAuthorizedAsync(HttpClient client, string url, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<(HttpClient Client, string AccessToken)> LoginAsync(Guid userId, Guid gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await dbContext.Users.SingleAsync(u => u.Id == userId);
        var game = await dbContext.Games.SingleAsync(g => g.Id == gameId);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(game.Slug, user.Email, Password),
            JsonOptions,
            TestContext.Current.CancellationToken);

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        return (client, tokens!.AccessToken);
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

    private async Task<User> SeedUserAsync(Guid gameId, PlatformRole role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = factory.TimeProvider.GetUtcNow();

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
}
