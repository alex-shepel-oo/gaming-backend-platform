using AwesomeAssertions;
using IdentityService.Persistence;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

// Covers Group A7 Session 5 (A.9): the five seeded, already-confirmed users
// now get fixed UserIds instead of a random Guid.CreateVersion7(), so
// EconomyService.DevelopmentSeeder has something stable to grant a welcome
// balance against (no cross-database foreign key - ADR-0001). This proves
// the ids are fixed constants, not that re-seeding itself does anything
// interesting - the seeder still no-ops once Games is non-empty.
[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class DevSeedFixedUserIdsTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private static readonly Dictionary<string, Guid> ExpectedUserIds = new()
    {
        ["admin@demo-shooter.dev"] = Guid.Parse("00000000-0000-7000-9000-000000000001"),
        ["player.one@demo-shooter.dev"] = Guid.Parse("00000000-0000-7000-9000-000000000002"),
        ["player.two@demo-shooter.dev"] = Guid.Parse("00000000-0000-7000-9000-000000000003"),
        ["gameadmin@demo-racer.dev"] = Guid.Parse("00000000-0000-7000-9000-000000000004"),
        ["player.three@demo-racer.dev"] = Guid.Parse("00000000-0000-7000-9000-000000000005"),
    };

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SeedAsync_RunTwiceAgainstFreshThenResetDatabase_AssignsSameFixedUserIdsBothTimes()
    {
        await factory.ResetAsync();
        await SeedAsync();
        var firstRun = await LoadUserIdsByEmailAsync();

        await factory.ResetAsync();
        await SeedAsync();
        var secondRun = await LoadUserIdsByEmailAsync();

        foreach (var (email, expectedId) in ExpectedUserIds)
        {
            firstRun[email].Should().Be(expectedId);
            secondRun[email].Should().Be(expectedId);
        }
    }

    private async Task SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
        await seeder.SeedAsync();
    }

    private async Task<Dictionary<string, Guid>> LoadUserIdsByEmailAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.Users
            .Where(u => ExpectedUserIds.Keys.Contains(u.Email))
            .ToDictionaryAsync(u => u.Email, u => u.Id);
    }
}
