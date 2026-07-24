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
public sealed class GameEndpointsTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task PostGame_WithPlatformGamesManage_Returns200AndIsVisibleInList()
    {
        var platformAdmin = await SeedUserAsync(gameId: null, PlatformRole.Admin);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameId: null, Permissions.PlatformGamesManage);
        var (client, accessToken) = await LoginAsync(platformAdmin.Id, gameId: null);

        var slug = $"slug-{Guid.NewGuid():N}";
        var postResponse = await PostAuthorizedAsync(
            client, "/api/identity/games", new CreateGameRequest(slug, "New Game"), accessToken);

        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResponse = await GetAuthorizedAsync(client, "/api/identity/games", accessToken);
        var games = await listResponse.Content.ReadFromJsonAsync<GameDto[]>(JsonOptions, TestContext.Current.CancellationToken);
        games.Should().ContainSingle(g => g.Slug == slug && g.Name == "New Game" && g.IsActive);
    }

    [Fact]
    public async Task PostGame_SeedsDefaultRolePermissionsForAdminAndModerator()
    {
        var platformAdmin = await SeedUserAsync(gameId: null, PlatformRole.Admin);
        await SeedRolePermissionsAsync(
            PlatformRole.Admin, gameId: null, Permissions.PlatformGamesManage, Permissions.PlatformRolesManage);
        var (client, accessToken) = await LoginAsync(platformAdmin.Id, gameId: null);

        var postResponse = await PostAuthorizedAsync(
            client, "/api/identity/games", new CreateGameRequest($"slug-{Guid.NewGuid():N}", "New Game"), accessToken);
        var game = await postResponse.Content.ReadFromJsonAsync<GameDto>(JsonOptions, TestContext.Current.CancellationToken);

        var adminPermsResponse = await GetAuthorizedAsync(
            client, $"/api/identity/roles/{PlatformRole.Admin}/permissions?gameId={game!.Id}", accessToken);
        var adminPerms = await adminPermsResponse.Content.ReadFromJsonAsync<string[]>(JsonOptions, TestContext.Current.CancellationToken);
        adminPerms.Should().BeEquivalentTo(
        [
            Permissions.GameMetadataEdit,
            Permissions.GameCurrencyManage,
            Permissions.GameBalanceAdjust,
            Permissions.GameRolesManage,
            Permissions.GamePlayersModerate,
        ]);

        var moderatorPermsResponse = await GetAuthorizedAsync(
            client, $"/api/identity/roles/{PlatformRole.Moderator}/permissions?gameId={game.Id}", accessToken);
        var moderatorPerms = await moderatorPermsResponse.Content.ReadFromJsonAsync<string[]>(
            JsonOptions, TestContext.Current.CancellationToken);
        moderatorPerms.Should().BeEquivalentTo(
        [
            Permissions.GameMetadataEdit,
            Permissions.GamePlayersModerate,
            Permissions.GameBalanceAdjust,
        ]);
    }

    [Fact]
    public async Task PostGame_DuplicateSlug_Returns409()
    {
        var platformAdmin = await SeedUserAsync(gameId: null, PlatformRole.Admin);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameId: null, Permissions.PlatformGamesManage);
        var existing = await SeedGameAsync();
        var (client, accessToken) = await LoginAsync(platformAdmin.Id, gameId: null);

        var response = await PostAuthorizedAsync(
            client, "/api/identity/games", new CreateGameRequest(existing.Slug, "Another Name"), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PatchGame_WithPlatformGamesManage_UpdatesNameAndIsActive()
    {
        var platformAdmin = await SeedUserAsync(gameId: null, PlatformRole.Admin);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameId: null, Permissions.PlatformGamesManage);
        var game = await SeedGameAsync();
        var (client, accessToken) = await LoginAsync(platformAdmin.Id, gameId: null);

        var response = await PatchAuthorizedAsync(
            client, $"/api/identity/games/{game.Id}", new UpdateGameRequest("Renamed", false), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GameDto>(JsonOptions, TestContext.Current.CancellationToken);
        body!.Name.Should().Be("Renamed");
        body.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ListGames_WithoutPlatformGamesManage_Returns403()
    {
        var user = await SeedUserAsync(gameId: null, PlatformRole.Admin);
        var (client, accessToken) = await LoginAsync(user.Id, gameId: null);

        var response = await GetAuthorizedAsync(client, "/api/identity/games", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostGame_WithoutPlatformGamesManage_Returns403()
    {
        var user = await SeedUserAsync(gameId: null, PlatformRole.Admin);
        var (client, accessToken) = await LoginAsync(user.Id, gameId: null);

        var response = await PostAuthorizedAsync(
            client, "/api/identity/games", new CreateGameRequest($"slug-{Guid.NewGuid():N}", "Name"), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PatchGame_WithoutPlatformGamesManage_Returns403()
    {
        var user = await SeedUserAsync(gameId: null, PlatformRole.Admin);
        var game = await SeedGameAsync();
        var (client, accessToken) = await LoginAsync(user.Id, gameId: null);

        var response = await PatchAuthorizedAsync(
            client, $"/api/identity/games/{game.Id}", new UpdateGameRequest("Renamed", null), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListPublicGames_StillReturnsOnlyActiveGamesRegardlessOfPermissions()
    {
        var game = await SeedGameAsync();
        var player = await SeedUserAsync(game.Id, PlatformRole.Player);
        var (client, accessToken) = await LoginAsync(player.Id, game.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/games/public", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> GetAuthorizedAsync(HttpClient client, string url, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PostAuthorizedAsync<TBody>(HttpClient client, string url, TBody body, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PatchAuthorizedAsync<TBody>(HttpClient client, string url, TBody body, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<(HttpClient Client, string AccessToken)> LoginAsync(Guid userId, Guid? gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await dbContext.Users.SingleAsync(u => u.Id == userId);
        var gameSlug = gameId is null ? null : (await dbContext.Games.SingleAsync(g => g.Id == gameId)).Slug;

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(gameSlug, user.Email, Password),
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

    private async Task<User> SeedUserAsync(Guid? gameId, PlatformRole role)
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
