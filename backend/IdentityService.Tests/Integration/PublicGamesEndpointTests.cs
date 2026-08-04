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
public sealed class PublicGamesEndpointTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonWebTokenHandler TokenHandler = new();

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ListPublicGames_ReturnsOnlyActiveGamesWithPublicFieldsOnly()
    {
        var activeGame = await SeedGameAsync(isActive: true);
        var inactiveGame = await SeedGameAsync(isActive: false);
        var player = await SeedUserAsync(activeGame.Id);
        var (client, accessToken) = await LoginAsync(player.Id, activeGame.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/games/public", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var items = json.EnumerateArray().ToArray();

        items.Select(g => g.GetProperty("id").GetGuid()).Should().Contain(activeGame.Id);
        items.Select(g => g.GetProperty("id").GetGuid()).Should().NotContain(inactiveGame.Id);

        var entry = items.Single(g => g.GetProperty("id").GetGuid() == activeGame.Id);
        entry.GetProperty("slug").GetString().Should().Be(activeGame.Slug);
        entry.GetProperty("name").GetString().Should().Be(activeGame.Name);
        entry.TryGetProperty("isActive", out _).Should().BeFalse();
        entry.TryGetProperty("createdAt", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ListPublicGames_ReturnsDescriptionAndIconUrlWhenSetAndNullWhenNot()
    {
        var gameWithMetadata = await SeedGameAsync(
            isActive: true, description: "A great game", iconUrl: "https://cdn.example.com/icon.png");
        var gameWithoutMetadata = await SeedGameAsync(isActive: true);
        var player = await SeedUserAsync(gameWithMetadata.Id);
        var (client, accessToken) = await LoginAsync(player.Id, gameWithMetadata.Id);

        var response = await GetAuthorizedAsync(client, "/api/identity/games/public", accessToken);

        var games = (await response.Content.ReadFromJsonAsync<PublicGameDto[]>(JsonOptions, TestContext.Current.CancellationToken))!;

        var withMetadata = games.Single(g => g.Id == gameWithMetadata.Id);
        withMetadata.Description.Should().Be("A great game");
        withMetadata.IconUrl.Should().Be("https://cdn.example.com/icon.png");

        var withoutMetadata = games.Single(g => g.Id == gameWithoutMetadata.Id);
        withoutMetadata.Description.Should().BeNull();
        withoutMetadata.IconUrl.Should().BeNull();
    }

    [Fact]
    public async Task ListPublicGames_Anonymous_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/identity/games/public", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListPublicGames_AccountScopedSession_Returns200()
    {
        var game = await SeedGameAsync(isActive: true);
        var player = await SeedUserAsync(game.Id);
        var (client, accessToken) = await LoginAccountScopedAsync(player.Email);

        var jwt = TokenHandler.ReadJsonWebToken(accessToken);
        jwt.GetClaim(IdentityClaims.Scope).Value.Should().Be(nameof(TokenScope.Account));
        jwt.TryGetClaim(IdentityClaims.Role, out _).Should().BeFalse();

        var response = await GetAuthorizedAsync(client, "/api/identity/games/public", accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListPublicGames_IsNotFilteredByCallersGame()
    {
        var gameA = await SeedGameAsync(isActive: true);
        var gameB = await SeedGameAsync(isActive: true);
        var playerA = await SeedUserAsync(gameA.Id);
        var playerB = await SeedUserAsync(gameB.Id);

        var (clientA, accessTokenA) = await LoginAsync(playerA.Id, gameA.Id);
        var (clientB, accessTokenB) = await LoginAsync(playerB.Id, gameB.Id);

        var responseA = await GetAuthorizedAsync(clientA, "/api/identity/games/public", accessTokenA);
        var responseB = await GetAuthorizedAsync(clientB, "/api/identity/games/public", accessTokenB);

        var idsA = await GameIdsAsync(responseA);
        var idsB = await GameIdsAsync(responseB);

        idsA.Should().BeEquivalentTo(idsB);
        idsA.Should().Contain([gameA.Id, gameB.Id]);
    }

    private static async Task<Guid[]> GameIdsAsync(HttpResponseMessage response)
    {
        var games = await response.Content.ReadFromJsonAsync<PublicGameDto[]>(JsonOptions, TestContext.Current.CancellationToken);

        return games!.Select(g => g.Id).ToArray();
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

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);

        return (client, tokens!.AccessToken);
    }

    private async Task<(HttpClient Client, string AccessToken)> LoginAccountScopedAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(null, email, Password),
            JsonOptions,
            TestContext.Current.CancellationToken);

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);

        return (client, tokens!.AccessToken);
    }

    private async Task<Game> SeedGameAsync(bool isActive, string? description = null, string? iconUrl = null)
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
            Description = description,
            IconUrl = iconUrl,
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
