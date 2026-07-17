using AwesomeAssertions;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Exceptions;
using IdentityService.Persistence;
using IdentityService.Services;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class RefreshReuseDetectionTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Rotate_AlreadyUsedToken_RevokesWholeFamily()
    {
        var (userId, gameId) = await SeedPlayerAsync();
        var issued = await IssueFamilyAsync(userId, gameId);

        var rotated = await RotateAsync(issued.RawToken);

        var act = async () => await RotateAsync(issued.RawToken);

        await act.Should().ThrowAsync<InvalidRefreshTokenException>();

        var family = await FindFamilyAsync(issued.Family.Id);
        family.RevokedAt.Should().NotBeNull();
        family.RevokedReason.Should().Be(RevocationReason.TokenReuse);

        var tokens = await FindTokensForFamilyAsync(issued.Family.Id);
        tokens.Should().HaveCount(2);
        tokens.Should().OnlyContain(t => t.RevokedAt != null);
        tokens.Should().Contain(t => t.Id == rotated.Token.Id);
    }

    [Fact]
    public async Task Rotate_AfterFamilyRevoked_LatestTokenAlsoRejected()
    {
        var (userId, gameId) = await SeedPlayerAsync();
        var issued = await IssueFamilyAsync(userId, gameId);

        var rotated = await RotateAsync(issued.RawToken);

        var replayOldToken = async () => await RotateAsync(issued.RawToken);
        await replayOldToken.Should().ThrowAsync<InvalidRefreshTokenException>();

        var useLatestToken = async () => await RotateAsync(rotated.RawRefreshToken);

        await useLatestToken.Should().ThrowAsync<InvalidRefreshTokenException>();
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

    private async Task<RefreshRotationResult> RotateAsync(string rawToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var refreshTokenService = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        return await refreshTokenService.RotateAsync(rawToken, "127.0.0.1");
    }

    private async Task<RefreshTokenFamily> FindFamilyAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.RefreshTokenFamilies.SingleAsync(f => f.Id == id);
    }

    private async Task<List<RefreshToken>> FindTokensForFamilyAsync(Guid familyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.RefreshTokens.Where(t => t.FamilyId == familyId).ToListAsync();
    }
}
