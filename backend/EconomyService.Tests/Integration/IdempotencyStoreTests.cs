using AwesomeAssertions;
using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Persistence;
using EconomyService.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Respawn;
using Testcontainers.PostgreSql;

namespace EconomyService.Tests.Integration;

[TestFixture]
public sealed class IdempotencyStoreTests : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("economy_db")
        .WithUsername("economy")
        .WithPassword("economy_test_password")
        .Build();

    private string _connectionString = null!;
    private Respawner _respawner = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
        });
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await DisposeAsync();

    [SetUp]
    public async Task SetUpAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Test]
    public async Task FindExistingAsync_UnknownKey_ReturnsNull()
    {
        await using var dbContext = CreateDbContext();
        var store = new IdempotencyStore(dbContext);

        var result = await store.FindExistingAsync(
            "unknown-key", TestContext.CurrentContext.CancellationToken);

        result.Should().BeNull();
    }

    [Test]
    public async Task FindExistingAsync_KnownKey_ReturnsMatchingLedgerEntry()
    {
        var currencyId = await SeedCurrencyAsync();
        var userId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext())
        {
            seedContext.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                CurrencyId = currencyId,
                Amount = 50m,
                TransactionType = TransactionType.Grant,
                IdempotencyKey = "grant-key-1",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seedContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        }

        await using var dbContext = CreateDbContext();
        var store = new IdempotencyStore(dbContext);

        var result = await store.FindExistingAsync(
            "grant-key-1", TestContext.CurrentContext.CancellationToken);

        result.Should().NotBeNull();
        result!.UserId.Should().Be(userId);
        result.Amount.Should().Be(50m);
    }

    private async Task<Guid> SeedCurrencyAsync()
    {
        await using var dbContext = CreateDbContext();
        var currency = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = "TEST_CREDITS",
            DisplayName = "Test Credits",
            Scope = CurrencyScope.Platform,
            GameId = null,
            Decimals = 2,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.Currencies.Add(currency);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        return currency.Id;
    }

    private EconomyDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EconomyDbContext>().UseNpgsql(_connectionString);
        return new EconomyDbContext(optionsBuilder.Options);
    }
}
