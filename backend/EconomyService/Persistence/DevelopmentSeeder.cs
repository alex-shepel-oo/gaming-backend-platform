using EconomyService.Domain;
using EconomyService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Persistence;

public sealed class DevelopmentSeeder(EconomyDbContext dbContext, TimeProvider timeProvider)
{
    // Must match the demo-shooter Game.Id identity's seeder creates. No
    // cross-database foreign key (ADR-0001) - just an agreed constant.
    private static readonly Guid DemoShooterGameId = Guid.Parse("00000000-0000-7000-8000-000000000001");

    // Same convention, matching identity's demo-racer Game.Id.
    private static readonly Guid DemoRacerGameId = Guid.Parse("00000000-0000-7000-8000-000000000002");

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
            Decimals = 2,
            CreatedAt = now,
        };

        var shooterGold = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = "SHOOTER_GOLD",
            DisplayName = "Shooter Gold",
            Scope = CurrencyScope.Game,
            GameId = DemoShooterGameId,
            Decimals = 2,
            CreatedAt = now,
        };

        var racerTokens = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = "RACER_TOKENS",
            DisplayName = "Racer Tokens",
            Scope = CurrencyScope.Game,
            GameId = DemoRacerGameId,
            Decimals = 2,
            CreatedAt = now,
        };

        dbContext.Currencies.AddRange(platformCredits, shooterGold, racerTokens);

        dbContext.ConversionRates.AddRange(
            new ConversionRate
            {
                Id = Guid.CreateVersion7(),
                FromCurrencyId = platformCredits.Id,
                ToCurrencyId = shooterGold.Id,
                Rate = 100m,
                CreatedAt = now,
            },
            new ConversionRate
            {
                Id = Guid.CreateVersion7(),
                FromCurrencyId = platformCredits.Id,
                ToCurrencyId = racerTokens.Id,
                Rate = 40m,
                CreatedAt = now,
            });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
