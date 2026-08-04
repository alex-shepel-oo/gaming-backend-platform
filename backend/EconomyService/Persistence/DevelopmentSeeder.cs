using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Services;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Persistence;

public sealed class DevelopmentSeeder(EconomyDbContext dbContext, TimeProvider timeProvider, IWelcomeGrantService welcomeGrantService)
{
    // Must match the demo-shooter Game.Id identity's seeder creates. No
    // cross-database foreign key (ADR-0001) - just an agreed constant.
    private static readonly Guid DemoShooterGameId = Guid.Parse("00000000-0000-7000-8000-000000000001");

    // Same convention, matching identity's demo-racer Game.Id.
    private static readonly Guid DemoRacerGameId = Guid.Parse("00000000-0000-7000-8000-000000000002");

    // Must match the fixed UserIds identity's seeder assigns its five demo
    // users - same "agreed constant, no cross-database FK" convention as the
    // game ids above, duplicated here for the same reason those are.
    private static readonly Guid SeedAdminUserId = Guid.Parse("00000000-0000-7000-9000-000000000001");
    private static readonly Guid PlayerOneUserId = Guid.Parse("00000000-0000-7000-9000-000000000002");
    private static readonly Guid PlayerTwoUserId = Guid.Parse("00000000-0000-7000-9000-000000000003");
    private static readonly Guid RacerAdminUserId = Guid.Parse("00000000-0000-7000-9000-000000000004");
    private static readonly Guid PlayerThreeUserId = Guid.Parse("00000000-0000-7000-9000-000000000005");

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
            IconUrl = "https://placehold.co/64x64?text=Credits",
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
            IconUrl = "https://placehold.co/64x64?text=Gold",
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
            IconUrl = "https://placehold.co/64x64?text=Tokens",
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
            },
            new ConversionRate
            {
                Id = Guid.CreateVersion7(),
                FromCurrencyId = shooterGold.Id,
                ToCurrencyId = platformCredits.Id,
                Rate = 0.01m,
                CreatedAt = now,
            },
            new ConversionRate
            {
                Id = Guid.CreateVersion7(),
                FromCurrencyId = racerTokens.Id,
                ToCurrencyId = platformCredits.Id,
                Rate = 0.025m,
                CreatedAt = now,
            });

        await dbContext.SaveChangesAsync(cancellationToken);

        // Seeded users never go through register/confirm-email, so the live
        // UserEmailConfirmed -> WelcomeGrantService path never fires for
        // them. Reusing GrantAsync directly here (after the currencies above
        // are committed - it looks PLATFORM_CREDITS up with a query) keeps
        // them consistent with the same "confirmed means granted" invariant
        // production gives every real account.
        foreach (var userId in new[] { SeedAdminUserId, PlayerOneUserId, PlayerTwoUserId, RacerAdminUserId, PlayerThreeUserId })
        {
            await welcomeGrantService.GrantAsync(userId, cancellationToken);
        }
    }
}
