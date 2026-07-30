using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
public sealed class UserEndpointsTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Me_ReturnsCurrentUserWithGameAndRoleFromToken()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, PlatformRole.Player, displayName: "Ada Lovelace");
        var (client, accessToken) = await LoginAsync(user.Id, game.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/users/me", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions, TestContext.Current.CancellationToken);
        body!.Id.Should().Be(user.Id);
        body.Email.Should().Be(user.Email);
        body.DisplayName.Should().Be("Ada Lovelace");
        body.GameId.Should().Be(game.Id);
        body.Role.Should().Be(PlatformRole.Player);
    }

    [Fact]
    public async Task Me_OnAccountScopedToken_ReturnsNullRoleInsteadOf500()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, PlatformRole.Player);
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(GameSlug: null, user.Email, Password),
            JsonOptions,
            TestContext.Current.CancellationToken);
        var tokens = await login.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);

        var response = await GetAuthorizedAsync(client, "/api/identity/users/me", tokens!.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions, TestContext.Current.CancellationToken);
        body!.Id.Should().Be(user.Id);
        body.Role.Should().BeNull();
        body.GameId.Should().BeNull();
    }

    [Fact]
    public async Task MyGames_UserWithOneGameRole_ReturnsThatGame()
    {
        var game = await SeedGameAsync("Racer X");
        var user = await SeedUserAsync(game.Id, PlatformRole.Admin);
        var (client, accessToken) = await LoginAsync(user.Id, game.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/users/me/games", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicGameDto[]>(JsonOptions, TestContext.Current.CancellationToken);
        body.Should().ContainSingle(g => g.Id == game.Id);
    }

    [Fact]
    public async Task MyGames_UserWithTwoGameRoles_ReturnsBothOrderedByName()
    {
        var gameB = await SeedGameAsync("Bravo Quest");
        var gameA = await SeedGameAsync("Alpha Quest");
        var user = await SeedUserAsync(gameB.Id, PlatformRole.Moderator);
        await AddGameRoleAsync(user.Id, gameA.Id, PlatformRole.Admin);
        var (client, accessToken) = await LoginAsync(user.Id, gameId: null);

        var response = await GetAuthorizedAsync(client, "/api/identity/users/me/games", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicGameDto[]>(JsonOptions, TestContext.Current.CancellationToken);
        body!.Select(g => g.Id).Should().ContainInOrder(gameA.Id, gameB.Id);
    }

    [Fact]
    public async Task MyGames_UserWithOnlyPlatformWideRole_ReturnsEmptyList()
    {
        var platformAdmin = await SeedUserAsync(gameId: null, PlatformRole.Admin);
        var (client, accessToken) = await LoginAsync(platformAdmin.Id, gameId: null);

        var response = await GetAuthorizedAsync(client, "/api/identity/users/me/games", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicGameDto[]>(JsonOptions, TestContext.Current.CancellationToken);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task MyGames_UserWithNoRolesAtAll_ReturnsEmptyList()
    {
        var user = await SeedUserWithoutRolesAsync();
        var (client, accessToken) = await LoginAsync(user.Id, gameId: null);

        var response = await GetAuthorizedAsync(client, "/api/identity/users/me/games", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicGameDto[]>(JsonOptions, TestContext.Current.CancellationToken);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateMe_WithNewDisplayName_UpdatesItAndLeavesAvatarUntouched()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, PlatformRole.Player, avatarUrl: "https://example.com/old-avatar.png");
        var (client, accessToken) = await LoginAsync(user.Id, game.Id);

        var response = await PatchAuthorizedAsync(
            client, "/api/identity/users/me", new UpdateProfileRequest("Updated Name", null), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions, TestContext.Current.CancellationToken);
        body!.DisplayName.Should().Be("Updated Name");
        body.AvatarUrl.Should().Be("https://example.com/old-avatar.png");
    }

    [Fact]
    public async Task UpdateMe_WithNewAvatarUrl_UpdatesIt()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, PlatformRole.Player);
        var (client, accessToken) = await LoginAsync(user.Id, game.Id);

        var response = await PatchAuthorizedAsync(
            client, "/api/identity/users/me", new UpdateProfileRequest(null, "https://example.com/avatar.png"), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions, TestContext.Current.CancellationToken);
        body!.AvatarUrl.Should().Be("https://example.com/avatar.png");
    }

    [Fact]
    public async Task UpdateMe_WithEmptyAvatarUrl_ClearsPreviouslySetAvatar()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, PlatformRole.Player, avatarUrl: "https://example.com/old-avatar.png");
        var (client, accessToken) = await LoginAsync(user.Id, game.Id);

        var response = await PatchAuthorizedAsync(
            client, "/api/identity/users/me", new UpdateProfileRequest(null, ""), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions, TestContext.Current.CancellationToken);
        body!.AvatarUrl.Should().BeNull();
    }

    [Fact]
    public async Task UpdateMe_WithAvatarUrlOmitted_LeavesPreviouslySetAvatarUntouched()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, PlatformRole.Player, avatarUrl: "https://example.com/old-avatar.png");
        var (client, accessToken) = await LoginAsync(user.Id, game.Id);

        var response = await PatchAuthorizedAsync(
            client, "/api/identity/users/me", new UpdateProfileRequest(null, null), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions, TestContext.Current.CancellationToken);
        body!.AvatarUrl.Should().Be("https://example.com/old-avatar.png");
    }

    [Fact]
    public async Task UpdateMe_WithMalformedAvatarUrl_Returns400()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, PlatformRole.Player);
        var (client, accessToken) = await LoginAsync(user.Id, game.Id);

        var response = await PatchAuthorizedAsync(
            client, "/api/identity/users/me", new UpdateProfileRequest(null, "not-a-url"), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMe_WithDisplayNameOutOfBounds_Returns400()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, PlatformRole.Player);
        var (client, accessToken) = await LoginAsync(user.Id, game.Id);

        var response = await PatchAuthorizedAsync(
            client, "/api/identity/users/me", new UpdateProfileRequest("A", null), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMe_TwoUsers_EachOnlyAffectsTheirOwnRecord()
    {
        var game = await SeedGameAsync();
        var userA = await SeedUserAsync(game.Id, PlatformRole.Player, displayName: "User A");
        var userB = await SeedUserAsync(game.Id, PlatformRole.Player, displayName: "User B");
        var (clientA, accessTokenA) = await LoginAsync(userA.Id, game.Id);
        var (clientB, accessTokenB) = await LoginAsync(userB.Id, game.Id);

        await PatchAuthorizedAsync(
            clientA, "/api/identity/users/me", new UpdateProfileRequest("User A Updated", null), accessTokenA);

        var responseA = await GetAuthorizedAsync(clientA, "/api/identity/users/me", accessTokenA);
        var bodyA = await responseA.Content.ReadFromJsonAsync<UserDto>(JsonOptions, TestContext.Current.CancellationToken);
        bodyA!.DisplayName.Should().Be("User A Updated");

        var responseB = await GetAuthorizedAsync(clientB, "/api/identity/users/me", accessTokenB);
        var bodyB = await responseB.Content.ReadFromJsonAsync<UserDto>(JsonOptions, TestContext.Current.CancellationToken);
        bodyB!.DisplayName.Should().Be("User B");
    }

    [Fact]
    public async Task GetUserById_UnknownUser_Returns404()
    {
        var game = await SeedGameAsync();
        var moderator = await SeedUserAsync(game.Id, PlatformRole.Moderator);
        var (client, accessToken) = await LoginAsync(moderator.Id, game.Id);

        var response = await GetAuthorizedAsync(
            client, $"/api/identity/users/{Guid.NewGuid()}", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListUsers_FiltersBySearchAcrossEmailAndDisplayName()
    {
        var game = await SeedGameAsync();
        var moderator = await SeedUserAsync(game.Id, PlatformRole.Moderator);
        var match = await SeedUserAsync(game.Id, PlatformRole.Player, displayName: "Grace Hopper");
        await SeedUserAsync(game.Id, PlatformRole.Player, displayName: "Someone Else");
        var (client, accessToken) = await LoginAsync(moderator.Id, game.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/users?search=grace", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<UserSummaryDto>>(
            JsonOptions, TestContext.Current.CancellationToken);
        body!.Items.Should().ContainSingle(u => u.Id == match.Id);
    }

    [Fact]
    public async Task ListUsers_ReturnsRoleAsJsonStringNotRawNumber()
    {
        var game = await SeedGameAsync();
        await SeedUserAsync(game.Id, PlatformRole.Admin, displayName: "Grace Hopper");
        var (client, accessToken) = await LoginAsync((await SeedUserAsync(game.Id, PlatformRole.Moderator)).Id, game.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/users?search=grace", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(raw);
        var role = document.RootElement.GetProperty("items")[0].GetProperty("role");
        role.ValueKind.Should().Be(JsonValueKind.String);
        role.GetString().Should().Be("Admin");
    }

    [Fact]
    public async Task ListUsers_RespectsPageAndPageSize()
    {
        var game = await SeedGameAsync();
        var moderator = await SeedUserAsync(game.Id, PlatformRole.Moderator);
        for (var i = 0; i < 3; i++)
        {
            await SeedUserAsync(game.Id, PlatformRole.Player);
        }

        var (client, accessToken) = await LoginAsync(moderator.Id, game.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/users?page=1&pageSize=2", accessToken);

        var body = await response.Content.ReadFromJsonAsync<PagedResult<UserSummaryDto>>(
            JsonOptions, TestContext.Current.CancellationToken);
        body!.Items.Should().HaveCount(2);
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(2);
        body.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task ListUsers_PageSizeOutOfRange_Returns400()
    {
        var game = await SeedGameAsync();
        var moderator = await SeedUserAsync(game.Id, PlatformRole.Moderator);
        var (client, accessToken) = await LoginAsync(moderator.Id, game.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/users?pageSize=101", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListGames_ReturnsAllGames()
    {
        var gameA = await SeedGameAsync();
        var gameB = await SeedGameAsync();
        var platformAdmin = await SeedUserAsync(gameId: null, PlatformRole.Admin);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameId: null, Permissions.PlatformGamesManage);
        var (client, accessToken) = await LoginAsync(platformAdmin.Id, gameId: null);

        var response = await GetAuthorizedAsync(client, "/api/identity/games", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GameDto[]>(JsonOptions, TestContext.Current.CancellationToken);
        body!.Select(g => g.Id).Should().Contain([gameA.Id, gameB.Id]);
    }

    private static Task<HttpResponseMessage> GetAuthorizedAsync(HttpClient client, string url, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PatchAuthorizedAsync<TBody>(
        HttpClient client, string url, TBody body, string accessToken)
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

    private async Task<Game> SeedGameAsync(string name = "Test Game")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        var game = new Game
        {
            Id = Guid.CreateVersion7(),
            Slug = $"game-{Guid.NewGuid():N}",
            Name = name,
            IsActive = true,
            CreatedAt = now,
        };

        dbContext.Games.Add(game);
        await dbContext.SaveChangesAsync();

        return game;
    }

    private async Task AddGameRoleAsync(Guid userId, Guid gameId, PlatformRole role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        dbContext.UserGameRoles.Add(new UserGameRole
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            GameId = gameId,
            Role = role,
            GrantedAt = now,
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task<User> SeedUserWithoutRolesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = factory.TimeProvider.GetUtcNow();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = "Roleless User",
            PasswordHash = passwordHasher.Hash(Password),
            IsActive = true,
            EmailConfirmed = true,
            EmailConfirmedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private async Task<User> SeedUserAsync(
        Guid? gameId, PlatformRole role, string displayName = "Test User", string? avatarUrl = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = factory.TimeProvider.GetUtcNow();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = displayName,
            PasswordHash = passwordHasher.Hash(Password),
            AvatarUrl = avatarUrl,
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
