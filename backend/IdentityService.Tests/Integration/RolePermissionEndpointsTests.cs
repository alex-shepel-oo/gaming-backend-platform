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
public sealed class RolePermissionEndpointsTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetPermissionCatalog_ReturnsAllTenPermissionKeys()
    {
        var game = await SeedGameAsync();
        var moderator = await SeedUserAsync(game.Id, PlatformRole.Moderator);
        var (client, accessToken) = await LoginAsync(moderator.Id, game.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/permissions", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<string[]>(JsonOptions, TestContext.Current.CancellationToken);
        body.Should().BeEquivalentTo(
        [
            Permissions.PlatformGamesManage,
            Permissions.PlatformCurrencyManage,
            Permissions.PlatformRolesManage,
            Permissions.PlatformUsersRead,
            Permissions.PlatformBalanceAdjust,
            Permissions.GameMetadataEdit,
            Permissions.GameCurrencyManage,
            Permissions.GameBalanceAdjust,
            Permissions.GameRolesManage,
            Permissions.GamePlayersModerate,
        ]);
    }

    [Fact]
    public async Task PutRolePermissions_GameAdminTargetsAnotherGame_Returns403()
    {
        var gameA = await SeedGameAsync();
        var gameB = await SeedGameAsync();
        var gameAdminA = await SeedUserAsync(gameA.Id, PlatformRole.Admin);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameA.Id, Permissions.GameRolesManage, Permissions.GameMetadataEdit);
        var (client, accessToken) = await LoginAsync(gameAdminA.Id, gameA.Id);

        var response = await PutAuthorizedAsync(
            client, $"/api/identity/roles/{PlatformRole.Admin}/permissions?gameId={gameB.Id}",
            new UpdateRolePermissionsRequest([Permissions.GameMetadataEdit]), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutRolePermissions_GameAdminGrantsPlatformPermissionForOwnGame_Returns403()
    {
        var gameA = await SeedGameAsync();
        var gameAdminA = await SeedUserAsync(gameA.Id, PlatformRole.Admin);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameA.Id, Permissions.GameRolesManage, Permissions.GameMetadataEdit);
        var (client, accessToken) = await LoginAsync(gameAdminA.Id, gameA.Id);

        var response = await PutAuthorizedAsync(
            client, $"/api/identity/roles/{PlatformRole.Admin}/permissions?gameId={gameA.Id}",
            new UpdateRolePermissionsRequest([Permissions.PlatformRolesManage]), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutRolePermissions_GameAdminEditsOwnGamePermissions_Returns200AndIsReflectedByGet()
    {
        var gameA = await SeedGameAsync();
        var gameAdminA = await SeedUserAsync(gameA.Id, PlatformRole.Admin);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameA.Id, Permissions.GameRolesManage, Permissions.GameMetadataEdit);
        var (client, accessToken) = await LoginAsync(gameAdminA.Id, gameA.Id);

        var putResponse = await PutAuthorizedAsync(
            client, $"/api/identity/roles/{PlatformRole.Moderator}/permissions?gameId={gameA.Id}",
            new UpdateRolePermissionsRequest([Permissions.GameMetadataEdit]), accessToken);

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await GetAuthorizedAsync(
            client, $"/api/identity/roles/{PlatformRole.Moderator}/permissions?gameId={gameA.Id}", accessToken);
        var body = await getResponse.Content.ReadFromJsonAsync<string[]>(JsonOptions, TestContext.Current.CancellationToken);
        body.Should().BeEquivalentTo([Permissions.GameMetadataEdit]);
    }

    [Fact]
    public async Task PutRolePermissions_PlatformAdminEditsAnotherGamesPermissions_Returns200()
    {
        var gameB = await SeedGameAsync();
        var platformAdmin = await SeedUserAsync(gameId: null, PlatformRole.Admin);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameId: null, Permissions.PlatformRolesManage, Permissions.GameMetadataEdit);
        var (client, accessToken) = await LoginAsync(platformAdmin.Id, gameId: null);

        var response = await PutAuthorizedAsync(
            client, $"/api/identity/roles/{PlatformRole.Moderator}/permissions?gameId={gameB.Id}",
            new UpdateRolePermissionsRequest([Permissions.GameMetadataEdit]), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PutRolePermissions_PlatformModeratorWithoutRolesManage_Returns403()
    {
        var game = await SeedGameAsync();
        var platformModerator = await SeedUserAsync(gameId: null, PlatformRole.Moderator);
        await SeedRolePermissionsAsync(PlatformRole.Moderator, gameId: null, Permissions.PlatformUsersRead, Permissions.GameMetadataEdit);
        var (client, accessToken) = await LoginAsync(platformModerator.Id, gameId: null);

        var response = await PutAuthorizedAsync(
            client, $"/api/identity/roles/{PlatformRole.Moderator}/permissions?gameId={game.Id}",
            new UpdateRolePermissionsRequest([Permissions.GameMetadataEdit]), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PatchUserRole_AssignedRolesEffectivePermissionsExceedCallerPerms_Returns403()
    {
        var gameA = await SeedGameAsync();
        var caller = await SeedUserAsync(gameA.Id, PlatformRole.Moderator);
        var target = await SeedUserAsync(gameA.Id, PlatformRole.Player);

        // Caller's own token only carries game.roles.manage, enough to pass the scope
        // check - the assigned role (Admin/A) resolves to a wider set below, which is
        // the part the guard is expected to reject.
        await SeedRolePermissionsAsync(PlatformRole.Moderator, gameA.Id, Permissions.GameRolesManage);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameA.Id, Permissions.GameRolesManage, Permissions.GameCurrencyManage);

        var (client, accessToken) = await LoginAsync(caller.Id, gameA.Id);

        var response = await PatchAuthorizedAsync(
            client, $"/api/identity/users/{target.Id}/roles",
            new AssignUserRoleRequest(gameA.Id, PlatformRole.Admin), accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PatchUserRole_WithinCallerPossessedPermissions_Returns200AndIsReflectedByGet()
    {
        var gameA = await SeedGameAsync();
        var caller = await SeedUserAsync(gameA.Id, PlatformRole.Admin);
        var target = await SeedUserAsync(gameA.Id, PlatformRole.Player);

        await SeedRolePermissionsAsync(PlatformRole.Admin, gameA.Id, Permissions.GameRolesManage, Permissions.GameCurrencyManage);
        await SeedRolePermissionsAsync(PlatformRole.Moderator, gameA.Id, Permissions.GameRolesManage);

        var (client, accessToken) = await LoginAsync(caller.Id, gameA.Id);

        var patchResponse = await PatchAuthorizedAsync(
            client, $"/api/identity/users/{target.Id}/roles",
            new AssignUserRoleRequest(gameA.Id, PlatformRole.Moderator), accessToken);

        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await GetAuthorizedAsync(client, $"/api/identity/users/{target.Id}/roles?gameId={gameA.Id}", accessToken);
        var body = await getResponse.Content.ReadFromJsonAsync<UserRoleDto>(JsonOptions, TestContext.Current.CancellationToken);
        body!.Role.Should().Be(PlatformRole.Moderator);
    }

    [Fact]
    public async Task PatchUserRole_WithLiteralRoleNameInRequestBody_Returns200NotServerError()
    {
        var gameA = await SeedGameAsync();
        var caller = await SeedUserAsync(gameA.Id, PlatformRole.Admin);
        var target = await SeedUserAsync(gameA.Id, PlatformRole.Player);

        await SeedRolePermissionsAsync(PlatformRole.Admin, gameA.Id, Permissions.GameRolesManage, Permissions.GameCurrencyManage);

        var (client, accessToken) = await LoginAsync(caller.Id, gameA.Id);

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/identity/users/{target.Id}/roles")
        {
            Content = new StringContent(
                $$"""{"gameId":"{{gameA.Id}}","role":"Admin"}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUserRole_ReturnsRoleAsJsonStringNotRawNumber()
    {
        var gameA = await SeedGameAsync();
        var caller = await SeedUserAsync(gameA.Id, PlatformRole.Admin);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameA.Id, Permissions.GameRolesManage);

        var (client, accessToken) = await LoginAsync(caller.Id, gameA.Id);

        var response = await GetAuthorizedAsync(client, $"/api/identity/users/{caller.Id}/roles?gameId={gameA.Id}", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(raw);
        var roleElement = document.RootElement.GetProperty("role");
        roleElement.ValueKind.Should().Be(JsonValueKind.String);
        roleElement.GetString().Should().Be("Admin");
    }

    [Fact]
    public async Task GetUserRole_NoRoleInScope_Returns404()
    {
        var gameA = await SeedGameAsync();
        var gameB = await SeedGameAsync();
        var caller = await SeedUserAsync(gameA.Id, PlatformRole.Admin);
        var target = await SeedUserAsync(gameB.Id, PlatformRole.Player);
        await SeedRolePermissionsAsync(PlatformRole.Admin, gameA.Id, Permissions.GameRolesManage);

        var (client, accessToken) = await LoginAsync(caller.Id, gameA.Id);

        var response = await GetAuthorizedAsync(client, $"/api/identity/users/{target.Id}/roles?gameId={gameA.Id}", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> GetAuthorizedAsync(HttpClient client, string url, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PutAuthorizedAsync<TBody>(HttpClient client, string url, TBody body, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
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
