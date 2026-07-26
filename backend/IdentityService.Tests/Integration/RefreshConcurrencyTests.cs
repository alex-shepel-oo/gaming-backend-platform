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
public sealed class RefreshConcurrencyTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TwoParallelRefreshes_ExactlyOneSucceeds()
    {
        var (userId, gameId) = await SeedPlayerAsync();
        var issued = await IssueFamilyAsync(userId, gameId);

        var firstAttempt = TryRotateAsync(issued.RawToken);
        var secondAttempt = TryRotateAsync(issued.RawToken);

        var outcomes = await Task.WhenAll(firstAttempt, secondAttempt);

        outcomes.Count(succeeded => succeeded).Should().Be(1);
        outcomes.Count(succeeded => !succeeded).Should().Be(1);

        var family = await FindFamilyAsync(issued.Family.Id);
        family.RevokedAt.Should().NotBeNull();
        family.RevokedReason.Should().Be(RevocationReason.TokenReuse);
        
        var tokenCount = await CountTokensForFamilyAsync(issued.Family.Id);
        tokenCount.Should().Be(2);
    }

    private async Task<bool> TryRotateAsync(string rawToken)
    {
        try
        {
            await RotateAsync(rawToken);
            return true;
        }
        catch (InvalidRefreshTokenException)
        {
            return false;
        }
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

        return await refreshTokenService.IssueFamilyAsync(userId, gameId, TokenScope.Game, "127.0.0.1", "test-agent");
    }

    private async Task RotateAsync(string rawToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var refreshTokenService = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        await refreshTokenService.RotateAsync(rawToken, "127.0.0.1");
    }

    private async Task<RefreshTokenFamily> FindFamilyAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.RefreshTokenFamilies.SingleAsync(f => f.Id == id);
    }

    private async Task<int> CountTokensForFamilyAsync(Guid familyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.RefreshTokens.CountAsync(t => t.FamilyId == familyId);
    }
}
