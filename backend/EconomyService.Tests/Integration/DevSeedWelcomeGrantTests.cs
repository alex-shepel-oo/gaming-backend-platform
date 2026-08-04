using AwesomeAssertions;
using EconomyService.Options;
using EconomyService.Persistence;
using EconomyService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace EconomyService.Tests.Integration;

// Covers Group A7 Session 5 (A.9): EconomyService.DevelopmentSeeder grants a
// welcome balance to all five of identity's fixed seeded users directly,
// through the same idempotent WelcomeGrantService -> ILedgerService path the
// live UserEmailConfirmedConsumer uses - no confirm-email call, no consumer,
// involved anywhere here.
[TestFixture]
public sealed class DevSeedWelcomeGrantTests : IAsyncDisposable
{
    private static readonly Guid[] SeededUserIds =
    [
        Guid.Parse("00000000-0000-7000-9000-000000000001"),
        Guid.Parse("00000000-0000-7000-9000-000000000002"),
        Guid.Parse("00000000-0000-7000-9000-000000000003"),
        Guid.Parse("00000000-0000-7000-9000-000000000004"),
        Guid.Parse("00000000-0000-7000-9000-000000000005"),
    ];

    private EconomyApiFactory _factory = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _factory = new EconomyApiFactory();
        await _factory.InitializeAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        await _factory.StopContainerAsync();
        await _factory.DisposeAsync();
    }

    [SetUp]
    public async Task SetUpAsync() => await _factory.ResetAsync();

    [Test]
    public async Task SeedAsync_GrantsConfiguredWelcomeAmountToAllFiveSeededUsers()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();

        await seeder.SeedAsync();

        var welcomeGrantOptions = scope.ServiceProvider.GetRequiredService<IOptions<WelcomeGrantOptions>>().Value;

        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();
        var currency = await dbContext.Currencies.SingleAsync(c => c.Code == welcomeGrantOptions.CurrencyCode);

        foreach (var userId in SeededUserIds)
        {
            var balance = await dbContext.Balances
                .SingleAsync(b => b.UserId == userId && b.CurrencyId == currency.Id);

            balance.Amount.Should().Be(welcomeGrantOptions.Amount);
        }
    }
}
