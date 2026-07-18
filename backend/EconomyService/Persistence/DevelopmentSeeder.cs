using EconomyService.Domain;
using EconomyService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Persistence;

public sealed class DevelopmentSeeder(EconomyDbContext dbContext, TimeProvider timeProvider)
{
    // Must match the demo-shooter Game.Id identity's seeder creates. No
    // cross-database foreign key (ADR-0001) - just an agreed constant.
    private static readonly Guid DemoShooterGameId = Guid.Parse("00000000-0000-7000-8000-000000000001");

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.Currencies.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        var platformCredits = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = "PLATFORM_CREDITS",
            DisplayName = "Platform Credits",
            Scope = CurrencyScope.Platform,
            GameId = null,
            CreatedAt = now,
        };

        var shooterGold = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = "SHOOTER_GOLD",
            DisplayName = "Shooter Gold",
            Scope = CurrencyScope.Game,
            GameId = DemoShooterGameId,
            CreatedAt = now,
        };

        dbContext.Currencies.AddRange(platformCredits, shooterGold);

        dbContext.ConversionRates.Add(new ConversionRate
        {
            Id = Guid.CreateVersion7(),
            FromCurrencyId = platformCredits.Id,
            ToCurrencyId = shooterGold.Id,
            Rate = 100m,
            CreatedAt = now,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
