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
        };

        var admin = CreateUser("admin@demo-shooter.dev", "Demo Admin", now);
        var playerOne = CreateUser("player.one@demo-shooter.dev", "Player One", now);
        var playerTwo = CreateUser("player.two@demo-shooter.dev", "Player Two", now);

        dbContext.Games.Add(game);
        dbContext.Users.AddRange(admin, playerOne, playerTwo);
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
            });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private User CreateUser(string email, string displayName, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
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
