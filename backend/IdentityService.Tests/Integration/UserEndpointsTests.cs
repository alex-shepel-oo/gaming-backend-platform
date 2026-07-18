using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        var admin = await SeedUserAsync(gameA.Id, PlatformRole.Admin);
        var (client, accessToken) = await LoginAsync(admin.Id, gameA.Id);

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

    private async Task<User> SeedUserAsync(Guid gameId, PlatformRole role, string displayName = "Test User")
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
}
