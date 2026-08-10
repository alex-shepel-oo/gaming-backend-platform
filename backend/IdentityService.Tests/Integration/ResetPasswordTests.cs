using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Messaging.Events;
using IdentityService.Persistence;
using IdentityService.Services;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class ResetPasswordTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string OldPassword = "correct-horse-battery";
    private const string NewPassword = "new-correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Reset_ValidToken_Returns200WithEmailAndChangesPassword()
    {
        using var client = factory.CreateClient();
        var (user, game) = await SeedUserAsync();
        var rawToken = await RequestTokenAsync(client, user.Email);

        var response = await ResetAsync(client, rawToken, NewPassword);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ResetPasswordResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body!.Email.Should().Be(user.Email);

        var oldLogin = await LoginAsync(client, game.Slug, user.Email, OldPassword);
        oldLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newLogin = await LoginAsync(client, game.Slug, user.Email, NewPassword);
        newLogin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reset_ValidToken_MarksTokenConsumedInDatabase()
    {
        using var client = factory.CreateClient();
        var (user, _) = await SeedUserAsync();
        var rawToken = await RequestTokenAsync(client, user.Email);

        await ResetAsync(client, rawToken, NewPassword);

        var storedToken = await FindTokenAsync(user.Id);
        storedToken!.ConsumedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Reset_ValidToken_RevokesRefreshFamiliesInBothGamesWithPasswordChangeReason()
    {
        using var client = factory.CreateClient();
        var (user, gameA, gameB) = await SeedUserWithTwoGamesAsync();

        var loginA = await LoginAsync(client, gameA.Slug, user.Email, OldPassword);
        var tokensA = await loginA.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);
        var loginB = await LoginAsync(client, gameB.Slug, user.Email, OldPassword);
        var tokensB = await loginB.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);

        var rawToken = await RequestTokenAsync(client, user.Email);
        var reset = await ResetAsync(client, rawToken, NewPassword);
        reset.StatusCode.Should().Be(HttpStatusCode.OK);

        var familyA = await FindFamilyAsync(user.Id, gameA.Id);
        var familyB = await FindFamilyAsync(user.Id, gameB.Id);

        familyA!.RevokedAt.Should().NotBeNull();
        familyA.RevokedReason.Should().Be(RevocationReason.PasswordChange);
        familyB!.RevokedAt.Should().NotBeNull();
        familyB.RevokedReason.Should().Be(RevocationReason.PasswordChange);

        var refreshA = await RefreshAsync(client, tokensA!.RefreshToken);
        var refreshB = await RefreshAsync(client, tokensB!.RefreshToken);
        refreshA.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refreshB.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reset_TokenReused_Returns400SameBodyAsUnknownToken()
    {
        using var client = factory.CreateClient();
        var (user, _) = await SeedUserAsync();
        var rawToken = await RequestTokenAsync(client, user.Email);

        var first = await ResetAsync(client, rawToken, NewPassword);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var reused = await ResetAsync(client, rawToken, "another-long-enough-password");
        var unknown = await ResetAsync(client, "unknown-raw-token-value", "another-long-enough-password");

        reused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        reused.StatusCode.Should().Be(unknown.StatusCode);

        var reusedBody = await NormalizedProblemBodyAsync(reused);
        var unknownBody = await NormalizedProblemBodyAsync(unknown);
        reusedBody.Should().Be(unknownBody);
    }

    [Fact]
    public async Task Reset_ExpiredToken_Returns400SameBodyAsUnknownToken()
    {
        using var client = factory.CreateClient();
        var (user, _) = await SeedUserAsync();
        var rawToken = await RequestTokenAsync(client, user.Email);

        factory.TimeProvider.Advance(TimeSpan.FromMinutes(31));

        var expired = await ResetAsync(client, rawToken, NewPassword);
        var unknown = await ResetAsync(client, "unknown-raw-token-value", NewPassword);

        expired.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        expired.StatusCode.Should().Be(unknown.StatusCode);

        var expiredBody = await NormalizedProblemBodyAsync(expired);
        var unknownBody = await NormalizedProblemBodyAsync(unknown);
        expiredBody.Should().Be(unknownBody);
    }

    [Fact]
    public async Task Reset_UnknownToken_Returns400()
    {
        using var client = factory.CreateClient();

        var response = await ResetAsync(client, "does-not-exist-at-all", NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidateToken_FreshToken_Returns204AndDoesNotConsumeIt()
    {
        using var client = factory.CreateClient();
        var (user, _) = await SeedUserAsync();
        var rawToken = await RequestTokenAsync(client, user.Email);

        var validated = await ValidateAsync(client, rawToken);
        validated.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The whole point of a read-only check: it must not use up the one-time token --
        // the actual reset right after still has to work.
        var reset = await ResetAsync(client, rawToken, NewPassword);
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ValidateToken_ConsumedToken_Returns400()
    {
        using var client = factory.CreateClient();
        var (user, _) = await SeedUserAsync();
        var rawToken = await RequestTokenAsync(client, user.Email);
        await ResetAsync(client, rawToken, NewPassword);

        var response = await ValidateAsync(client, rawToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidateToken_ExpiredToken_Returns400()
    {
        using var client = factory.CreateClient();
        var (user, _) = await SeedUserAsync();
        var rawToken = await RequestTokenAsync(client, user.Email);

        factory.TimeProvider.Advance(TimeSpan.FromMinutes(31));

        var response = await ValidateAsync(client, rawToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidateToken_UnknownToken_Returns400()
    {
        using var client = factory.CreateClient();

        var response = await ValidateAsync(client, "does-not-exist-at-all");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string token, string newPassword) =>
        client.PostAsJsonAsync(
            "/api/identity/auth/reset-password",
            new ResetPasswordRequest(token, newPassword),
            JsonOptions,
            TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> ValidateAsync(HttpClient client, string token) =>
        client.GetAsync(
            $"/api/identity/auth/reset-password/validate?token={Uri.EscapeDataString(token)}",
            TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string gameSlug, string email, string password) =>
        client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(gameSlug, email, password),
            JsonOptions,
            TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync(
            "/api/identity/auth/refresh", new RefreshRequest(refreshToken), JsonOptions, TestContext.Current.CancellationToken);

    private async Task<string> RequestTokenAsync(HttpClient client, string email)
    {
        await client.PostAsJsonAsync(
            "/api/identity/auth/request-password-reset",
            new RequestPasswordResetRequest(email),
            JsonOptions,
            TestContext.Current.CancellationToken);

        var events = await factory.GetOutboxEventsAsync<PasswordResetRequestedEvent>(
            "password_reset.requested", TestContext.Current.CancellationToken);
        var resetLink = events.Last(e => e.Email == email).ResetLink;

        const string marker = "token=";
        var start = resetLink.IndexOf(marker, StringComparison.Ordinal) + marker.Length;

        return resetLink[start..];
    }

    private static async Task<string> NormalizedProblemBodyAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var properties = json.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}");

        return string.Join('&', properties);
    }

    private async Task<(User User, Game Game)> SeedUserAsync()
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
            DisplayName = "Test Player",
            PasswordHash = passwordHasher.Hash(OldPassword),
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
            Role = PlatformRole.Player,
            GrantedAt = now,
        });

        await dbContext.SaveChangesAsync();

        return (user, game);
    }

    private async Task<(User User, Game GameA, Game GameB)> SeedUserWithTwoGamesAsync()
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
            PasswordHash = passwordHasher.Hash(OldPassword),
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

        return (user, gameA, gameB);
    }

    private async Task<PasswordResetToken?> FindTokenAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.PasswordResetTokens.SingleOrDefaultAsync(t => t.UserId == userId);
    }

    private async Task<RefreshTokenFamily?> FindFamilyAsync(Guid userId, Guid gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.RefreshTokenFamilies.SingleOrDefaultAsync(f => f.UserId == userId && f.GameId == gameId);
    }
}
