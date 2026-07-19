using AwesomeAssertions;
using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Respawn;
using Testcontainers.PostgreSql;

namespace EconomyService.Tests.Integration;

[TestFixture]
public sealed class SchemaTests : IAsyncDisposable
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
    public async Task Migrate_AppliesToEmptyContainer_CreatesAllFiveTables()
    {
        var tableNames = await GetTableNamesAsync();

        tableNames.Should().BeEquivalentTo(
            ["balances", "conversion_rates", "currencies", "ledger_entries", "outbox_messages"]);
    }

    [Test]
    public async Task SaveChanges_PlatformCurrencyWithGameId_ThrowsOnScopeCheck()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Currencies.Add(new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = "BAD_PLATFORM",
            DisplayName = "Bad Platform Currency",
            Scope = CurrencyScope.Platform,
            GameId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task SaveChanges_GameCurrencyWithoutGameId_ThrowsOnScopeCheck()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Currencies.Add(new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = "BAD_GAME",
            DisplayName = "Bad Game Currency",
            Scope = CurrencyScope.Game,
            GameId = null,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task SaveChanges_NegativeBalanceAmount_ThrowsOnAmountCheck()
    {
        await using var dbContext = CreateDbContext();
        var currency = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = "TEST_CREDITS",
            DisplayName = "Test Credits",
            Scope = CurrencyScope.Platform,
            GameId = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.Currencies.Add(currency);
        await dbContext.SaveChangesAsync();

        dbContext.Balances.Add(new Balance
        {
            Id = Guid.CreateVersion7(),
            UserId = Guid.NewGuid(),
            CurrencyId = currency.Id,
            Amount = -10m,
            Version = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private EconomyDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EconomyDbContext>().UseNpgsql(_connectionString);
        return new EconomyDbContext(optionsBuilder.Options);
    }

    private async Task<List<string>> GetTableNamesAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
            ORDER BY table_name;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();

        var tableNames = new List<string>();

        while (await reader.ReadAsync())
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }
}
