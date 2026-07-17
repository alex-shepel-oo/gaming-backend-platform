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
public sealed class RefreshEndpointTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Refresh_ValidToken_Returns200WithNewPair()
    {
        var (userId, gameId) = await SeedPlayerAsync();
        var issued = await IssueFamilyAsync(userId, gameId);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/refresh",
            new RefreshRequest(issued.RawToken),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBe(issued.RawToken);
    }

    [Fact]
    public async Task Refresh_InvalidToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/refresh",
            new RefreshRequest("not-a-real-token"),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_AlreadyUsedToken_Returns401()
    {
        var (userId, gameId) = await SeedPlayerAsync();
        var issued = await IssueFamilyAsync(userId, gameId);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/identity/auth/refresh",
            new RefreshRequest(issued.RawToken),
            JsonOptions,
            TestContext.Current.CancellationToken);

        var replay = await client.PostAsJsonAsync(
            "/api/identity/auth/refresh",
            new RefreshRequest(issued.RawToken),
            JsonOptions,
            TestContext.Current.CancellationToken);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_EmptyToken_Returns400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/refresh",
            new RefreshRequest(string.Empty),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<(Guid UserId, Guid GameId)> SeedPlayerAsync()
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

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = "Test Player",
            PasswordHash = "irrelevant",
            IsActive = true,
            EmailConfirmed = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var role = new UserGameRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            GameId = game.Id,
            Role = PlatformRole.Player,
            GrantedAt = now,
        };

        dbContext.Games.Add(game);
        dbContext.Users.Add(user);
        dbContext.UserGameRoles.Add(role);
        await dbContext.SaveChangesAsync();

        return (user.Id, game.Id);
    }

    private async Task<RefreshTokenIssueResult> IssueFamilyAsync(Guid userId, Guid gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var refreshTokenService = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        return await refreshTokenService.IssueFamilyAsync(userId, gameId, "127.0.0.1", "test-agent");
    }
}
