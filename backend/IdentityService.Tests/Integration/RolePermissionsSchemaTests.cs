using AwesomeAssertions;
using IdentityService.Auth;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace IdentityService.Tests.Integration;

// A dedicated container instead of the shared IdentityApiCollectionDefinition one:
// the default role_permissions rows are inserted by the migration itself (not by
// DevelopmentSeeder), and the shared fixture's Respawner truncates every table between
// tests, which would wipe that seed data before these assertions ever ran.
public sealed class RolePermissionsSchemaFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("identity_db")
        .WithUsername("identity")
        .WithPassword("identity_test_password")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(ConnectionString);
        await using var dbContext = new IdentityDbContext(optionsBuilder.Options);
        await dbContext.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

public sealed class RolePermissionsSchemaTests(RolePermissionsSchemaFixture fixture) : IClassFixture<RolePermissionsSchemaFixture>
{
    [Fact]
    public async Task SaveChanges_PlatformPermissionWithGameId_ThrowsOnScopeCheck()
    {
        await using var dbContext = CreateDbContext();
        dbContext.RolePermissions.Add(new RolePermission
        {
            Id = Guid.CreateVersion7(),
            Role = PlatformRole.Admin,
            GameId = Guid.NewGuid(),
            Permission = Permissions.PlatformGamesManage,
            GrantedAt = DateTimeOffset.UtcNow,
        });

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChanges_GamePermissionWithoutGameId_Succeeds()
    {
        // Player has no default rows, so this table stays clean for the other
        // tests in this class that assert on the exact default seed - insert
        // and remove within the same test rather than leaving it committed.
        await using var dbContext = CreateDbContext();
        var rolePermission = new RolePermission
        {
            Id = Guid.CreateVersion7(),
            Role = PlatformRole.Player,
            GameId = null,
            Permission = Permissions.GameBalanceAdjust,
            GrantedAt = DateTimeOffset.UtcNow,
        };
        dbContext.RolePermissions.Add(rolePermission);

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().NotThrowAsync();

        dbContext.RolePermissions.Remove(rolePermission);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SaveChanges_DuplicateOfSeededPlatformWideGrant_ThrowsOnUniqueIndex()
    {
        await using var dbContext = CreateDbContext();

        // Duplicates a row the AddRolePermissions migration already seeded for
        // Platform-Moderator - the partial unique index on (role, permission)
        // WHERE game_id IS NULL must reject it regardless of test order.
        dbContext.RolePermissions.Add(new RolePermission
        {
            Id = Guid.CreateVersion7(),
            Role = PlatformRole.Moderator,
            GameId = null,
            Permission = Permissions.PlatformUsersRead,
            GrantedAt = DateTimeOffset.UtcNow,
        });

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChanges_SamePermissionForDifferentGames_Succeeds()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var gameA = await SeedGameAsync(dbContext, now);
        var gameB = await SeedGameAsync(dbContext, now);

        dbContext.RolePermissions.AddRange(
            new RolePermission
            {
                Id = Guid.CreateVersion7(),
                Role = PlatformRole.Admin,
                GameId = gameA.Id,
                Permission = Permissions.GameMetadataEdit,
                GrantedAt = now,
            },
            new RolePermission
            {
                Id = Guid.CreateVersion7(),
                Role = PlatformRole.Admin,
                GameId = gameB.Id,
                Permission = Permissions.GameMetadataEdit,
                GrantedAt = now,
            });

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DefaultSeed_PlatformAdmin_HasAllTenCatalogPermissions()
    {
        await using var dbContext = CreateDbContext();

        var permissions = await dbContext.RolePermissions
            .Where(r => r.Role == PlatformRole.Admin && r.GameId == null)
            .Select(r => r.Permission)
            .ToListAsync(TestContext.Current.CancellationToken);

        permissions.Should().BeEquivalentTo(
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
    public async Task DefaultSeed_PlatformModerator_HasExactlyThreeCatalogPermissions()
    {
        await using var dbContext = CreateDbContext();

        var permissions = await dbContext.RolePermissions
            .Where(r => r.Role == PlatformRole.Moderator && r.GameId == null)
            .Select(r => r.Permission)
            .ToListAsync(TestContext.Current.CancellationToken);

        permissions.Should().BeEquivalentTo(
        [
            Permissions.PlatformUsersRead,
            Permissions.GameMetadataEdit,
            Permissions.GamePlayersModerate,
        ]);
    }

    [Fact]
    public async Task DefaultSeed_Player_HasNoRows()
    {
        await using var dbContext = CreateDbContext();

        var hasAny = await dbContext.RolePermissions
            .AnyAsync(r => r.Role == PlatformRole.Player && r.GameId == null, TestContext.Current.CancellationToken);

        hasAny.Should().BeFalse();
    }

    private static async Task<Game> SeedGameAsync(IdentityDbContext dbContext, DateTimeOffset now)
    {
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

    private IdentityDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(fixture.ConnectionString);
        return new IdentityDbContext(optionsBuilder.Options);
    }
}
