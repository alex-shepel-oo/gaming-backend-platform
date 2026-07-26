using AwesomeAssertions;
using IdentityService.Auth;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Exceptions;
using IdentityService.Persistence;
using IdentityService.Services;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class RefreshRotationTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Rotate_ValidToken_IssuesNewPairAndMarksOldUsed()
    {
        var (userId, gameId) = await SeedPlayerAsync();
        var issued = await IssueFamilyAsync(userId, gameId);

        var rotated = await RotateAsync(issued.RawToken);

        rotated.RawRefreshToken.Should().NotBe(issued.RawToken);
        rotated.Token.Generation.Should().Be(issued.Token.Generation + 1);
        rotated.Family.Id.Should().Be(issued.Family.Id);

        var oldToken = await FindTokenAsync(issued.Token.Id);
        oldToken.UsedAt.Should().NotBeNull();
        oldToken.ReplacedByTokenId.Should().Be(rotated.Token.Id);
    }

    [Fact]
    public async Task Rotate_TokenBeyondFamilyAbsoluteLifetime_Returns401()
    {
        var (userId, gameId) = await SeedPlayerAsync();
        var issued = await IssueFamilyAsync(userId, gameId);

        factory.TimeProvider.Advance(TimeSpan.FromDays(31));

        var act = async () => await RotateAsync(issued.RawToken);

        await act.Should().ThrowAsync<InvalidRefreshTokenException>();
    }

    [Fact]
    public async Task Rotate_ExpiredButUnusedToken_DoesNotRevokeFamily()
    {
        var (userId, gameId) = await SeedPlayerAsync();
        var issued = await IssueFamilyAsync(userId, gameId);

        factory.TimeProvider.Advance(TimeSpan.FromDays(15));

        var act = async () => await RotateAsync(issued.RawToken);

        await act.Should().ThrowAsync<InvalidRefreshTokenException>();

        var family = await FindFamilyAsync(issued.Family.Id);
        family.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Rotate_AfterRolePermissionsChangeInDatabase_NewAccessTokenReflectsUpdatedPermissions()
    {
        var (userId, gameId) = await SeedPlayerAsync();
        var issued = await IssueFamilyAsync(userId, gameId);

        var firstRotation = await RotateAsync(issued.RawToken);
        var firstJwt = new JsonWebTokenHandler().ReadJsonWebToken(firstRotation.AccessToken);
        firstJwt.Claims.Where(c => c.Type == IdentityClaims.Perms).Should().BeEmpty();

        await GrantRolePermissionAsync(gameId, Permissions.GameBalanceAdjust);

        var secondRotation = await RotateAsync(firstRotation.RawRefreshToken);
        var secondJwt = new JsonWebTokenHandler().ReadJsonWebToken(secondRotation.AccessToken);

        secondJwt.Claims.Where(c => c.Type == IdentityClaims.Perms).Select(c => c.Value).Should()
            .BeEquivalentTo([Permissions.GameBalanceAdjust]);
    }

    private async Task GrantRolePermissionAsync(Guid gameId, string permission)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        dbContext.RolePermissions.Add(new RolePermission
        {
            Id = Guid.CreateVersion7(),
            Role = PlatformRole.Player,
            GameId = gameId,
            Permission = permission,
            GrantedAt = now,
        });

        await dbContext.SaveChangesAsync();
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

    private async Task<RefreshRotationResult> RotateAsync(string rawToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var refreshTokenService = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        return await refreshTokenService.RotateAsync(rawToken, "127.0.0.1");
    }

    private async Task<RefreshToken> FindTokenAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.RefreshTokens.SingleAsync(t => t.Id == id);
    }

    private async Task<RefreshTokenFamily> FindFamilyAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.RefreshTokenFamilies.SingleAsync(f => f.Id == id);
    }
}
