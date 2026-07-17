using System.Net;
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
public sealed class LoginEndpointTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
    public async Task Login_PlatformLoginWithoutPlatformRole_Returns403NoAccessToGame()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, gameSlug: null, user.Email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

    private async Task<User> SeedUserAsync(Guid? gameId, bool confirmed, bool active)
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
            Role = PlatformRole.Player,
            GrantedAt = now,
        });

        await dbContext.SaveChangesAsync();

        return user;
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
}
