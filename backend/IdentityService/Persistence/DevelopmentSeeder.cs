using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Services;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Persistence;

public sealed class DevelopmentSeeder(IdentityDbContext dbContext, TimeProvider timeProvider, IPasswordHasher passwordHasher)
{
    // Dev-only seed credential for the local compose stack. Not a
    // production secret: nothing built so far can log in with it yet.
    private const string SeedPassword = "DemoPassword123!";

    // Second demo tenant, kept alongside demo-shooter so tenant-isolation scenarios have
    // two real games to exercise instead of just one.
    private static readonly Guid DemoRacerGameId = Guid.Parse("00000000-0000-7000-8000-000000000002");

    // Fixed ids (segment 9000, distinct from the 8000 games use) so
    // EconomyService.DevelopmentSeeder can address these users without a
    // cross-database foreign key (ADR-0001) - the same convention already
    // used for DemoShooterGameId/DemoRacerGameId above.
    private static readonly Guid SeedAdminUserId = Guid.Parse("00000000-0000-7000-9000-000000000001");
    private static readonly Guid PlayerOneUserId = Guid.Parse("00000000-0000-7000-9000-000000000002");
    private static readonly Guid PlayerTwoUserId = Guid.Parse("00000000-0000-7000-9000-000000000003");
    private static readonly Guid RacerAdminUserId = Guid.Parse("00000000-0000-7000-9000-000000000004");
    private static readonly Guid PlayerThreeUserId = Guid.Parse("00000000-0000-7000-9000-000000000005");

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.Games.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        var game = new Game
        {
            Id = Guid.Parse("00000000-0000-7000-8000-000000000001"),
            Slug = "demo-shooter",
            Name = "Demo Shooter",
            IsActive = true,
            CreatedAt = now,
            Description = "A fast-paced arena shooter used to exercise the platform's demo tenant.",
            IconUrl = "https://placehold.co/128x128?text=Shooter",
        };

        var demoRacer = new Game
        {
            Id = DemoRacerGameId,
            Slug = "demo-racer",
            Name = "Demo Racer",
            IsActive = true,
            CreatedAt = now,
            Description = "An arcade racing game used as the second demo tenant for isolation scenarios.",
            IconUrl = "https://placehold.co/128x128?text=Racer",
        };

        var admin = CreateUser(SeedAdminUserId, "admin@demo-shooter.dev", "Demo Admin", now);
        var playerOne = CreateUser(PlayerOneUserId, "player.one@demo-shooter.dev", "Player One", now);
        var playerTwo = CreateUser(PlayerTwoUserId, "player.two@demo-shooter.dev", "Player Two", now);
        var racerAdmin = CreateUser(RacerAdminUserId, "gameadmin@demo-racer.dev", "Demo Racer Admin", now);
        var playerThree = CreateUser(PlayerThreeUserId, "player.three@demo-racer.dev", "Player Three", now);

        dbContext.Games.AddRange(game, demoRacer);
        dbContext.Users.AddRange(admin, playerOne, playerTwo, racerAdmin, playerThree);
        dbContext.UserGameRoles.AddRange(
            new UserGameRole
            {
                Id = Guid.CreateVersion7(),
                UserId = admin.Id,
                GameId = null,
                Role = PlatformRole.Admin,
                GrantedAt = now,
            },
            new UserGameRole
            {
                Id = Guid.CreateVersion7(),
                UserId = playerOne.Id,
                GameId = game.Id,
                Role = PlatformRole.Player,
                GrantedAt = now,
            },
            new UserGameRole
            {
                Id = Guid.CreateVersion7(),
                UserId = playerTwo.Id,
                GameId = game.Id,
                Role = PlatformRole.Player,
                GrantedAt = now,
            },
            new UserGameRole
            {
                Id = Guid.CreateVersion7(),
                UserId = racerAdmin.Id,
                GameId = demoRacer.Id,
                Role = PlatformRole.Admin,
                GrantedAt = now,
            },
            new UserGameRole
            {
                Id = Guid.CreateVersion7(),
                UserId = playerThree.Id,
                GameId = demoRacer.Id,
                Role = PlatformRole.Player,
                GrantedAt = now,
            });

        // demo-shooter predates DefaultRolePermissions and never got these rows either -
        // backfilling it here closes the same gap CreateGameAsync now closes for new games.
        dbContext.RolePermissions.AddRange(DefaultRolePermissions.ForGame(game.Id, now));
        dbContext.RolePermissions.AddRange(DefaultRolePermissions.ForGame(demoRacer.Id, now));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private User CreateUser(Guid id, string email, string displayName, DateTimeOffset now) => new()
    {
        Id = id,
        Email = email,
        DisplayName = displayName,
        PasswordHash = passwordHasher.Hash(SeedPassword),
        IsActive = true,
        EmailConfirmed = true,
        EmailConfirmedAt = now,
        CreatedAt = now,
        UpdatedAt = now,
    };
}
