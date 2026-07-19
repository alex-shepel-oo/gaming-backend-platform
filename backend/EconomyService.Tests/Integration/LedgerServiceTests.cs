using AwesomeAssertions;
using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Exceptions;
using EconomyService.Persistence;
using EconomyService.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Respawn;
using Testcontainers.PostgreSql;

namespace EconomyService.Tests.Integration;

[TestFixture]
public sealed class LedgerServiceTests : IAsyncDisposable
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
    public async Task GrantAsync_NewKey_CreatesLedgerEntryAndUpdatesBalance_InSingleTransaction()
    {
        var currencyId = await SeedCurrencyAsync();
        var userId = Guid.NewGuid();
        var ledgerService = CreateLedgerService(out var dbContext);
        await using var _ = dbContext;

        var result = await ledgerService.GrantAsync(
            new LedgerMutationRequest(userId, currencyId, 100m, "grant-key-1", "welcome bonus"),
            TestContext.CurrentContext.CancellationToken);

        result.IsReplay.Should().BeFalse();
        result.Balance.Should().Be(100m);
        result.Entry.TransactionType.Should().Be(TransactionType.Grant);
        result.Entry.Amount.Should().Be(100m);

        await using var verifyContext = CreateDbContext();
        var entries = await verifyContext.LedgerEntries
            .Where(e => e.UserId == userId && e.CurrencyId == currencyId)
            .ToListAsync(TestContext.CurrentContext.CancellationToken);
        entries.Should().HaveCount(1);

        var balance = await verifyContext.Balances
            .SingleAsync(b => b.UserId == userId && b.CurrencyId == currencyId, TestContext.CurrentContext.CancellationToken);
        balance.Amount.Should().Be(100m);
        balance.Version.Should().Be(1);
    }

    [Test]
    public async Task Balance_EqualsSumOfLedgerEntriesForThatBalance()
    {
        var currencyId = await SeedCurrencyAsync();
        var userId = Guid.NewGuid();

        var ledgerService1 = CreateLedgerService(out var dbContext1);
        await using (dbContext1)
        {
            await ledgerService1.GrantAsync(
                new LedgerMutationRequest(userId, currencyId, 100m, "sum-key-grant-1"),
                TestContext.CurrentContext.CancellationToken);
        }

        var ledgerService2 = CreateLedgerService(out var dbContext2);
        await using (dbContext2)
        {
            await ledgerService2.GrantAsync(
                new LedgerMutationRequest(userId, currencyId, 50m, "sum-key-grant-2"),
                TestContext.CurrentContext.CancellationToken);
        }

        var ledgerService3 = CreateLedgerService(out var dbContext3);
        await using (dbContext3)
        {
            await ledgerService3.SpendAsync(
                new LedgerMutationRequest(userId, currencyId, 30m, "sum-key-spend-1"),
                TestContext.CurrentContext.CancellationToken);
        }

        await using var verifyContext = CreateDbContext();
        var sumOfEntries = await verifyContext.LedgerEntries
            .Where(e => e.UserId == userId && e.CurrencyId == currencyId)
            .SumAsync(e => e.Amount, TestContext.CurrentContext.CancellationToken);

        var balanceService = new BalanceService(verifyContext);
        var balance = await balanceService.GetBalanceAsync(userId, currencyId, TestContext.CurrentContext.CancellationToken);

        balance.Should().Be(120m);
        balance.Should().Be(sumOfEntries);
    }

    [Test]
    public async Task SpendAsync_TwoConcurrentSpendsOverHalfBalance_OnlyOneSucceeds_BalanceNeverNegative()
    {
        var currencyId = await SeedCurrencyAsync();
        var userId = Guid.NewGuid();

        var seedLedgerService = CreateLedgerService(out var seedDbContext);
        await using (seedDbContext)
        {
            await seedLedgerService.GrantAsync(
                new LedgerMutationRequest(userId, currencyId, 100m, "concurrency-seed-grant"),
                TestContext.CurrentContext.CancellationToken);
        }

        var spendTask1 = SpendSafelyAsync(userId, currencyId, 70m, "concurrency-spend-1");
        var spendTask2 = SpendSafelyAsync(userId, currencyId, 70m, "concurrency-spend-2");

        var outcomes = await Task.WhenAll(spendTask1, spendTask2);

        outcomes.Count(o => o.Succeeded).Should().Be(1);
        outcomes.Count(o => o.InsufficientFunds).Should().Be(1);

        await using var verifyContext = CreateDbContext();
        var balance = await verifyContext.Balances
            .SingleAsync(b => b.UserId == userId && b.CurrencyId == currencyId, TestContext.CurrentContext.CancellationToken);

        balance.Amount.Should().Be(30m);
        balance.Amount.Should().BeGreaterThanOrEqualTo(0m);

        var spendEntryCount = await verifyContext.LedgerEntries
            .Where(e => e.UserId == userId && e.CurrencyId == currencyId && e.TransactionType == TransactionType.Spend)
            .CountAsync(TestContext.CurrentContext.CancellationToken);
        spendEntryCount.Should().Be(1);
    }

    [Test]
    public async Task GrantAsync_RepeatedIdempotencyKey_SequentialCalls_ProducesSingleEntryAndSameResult()
    {
        var currencyId = await SeedCurrencyAsync();
        var userId = Guid.NewGuid();
        var request = new LedgerMutationRequest(userId, currencyId, 25m, "replay-key-1", "sign-up grant");

        var ledgerService1 = CreateLedgerService(out var dbContext1);
        LedgerPostResult firstResult;
        await using (dbContext1)
        {
            firstResult = await ledgerService1.GrantAsync(request, TestContext.CurrentContext.CancellationToken);
        }

        var ledgerService2 = CreateLedgerService(out var dbContext2);
        LedgerPostResult secondResult;
        await using (dbContext2)
        {
            secondResult = await ledgerService2.GrantAsync(request, TestContext.CurrentContext.CancellationToken);
        }

        firstResult.IsReplay.Should().BeFalse();
        secondResult.IsReplay.Should().BeTrue();
        secondResult.Entry.Id.Should().Be(firstResult.Entry.Id);
        secondResult.Balance.Should().Be(firstResult.Balance);

        await using var verifyContext = CreateDbContext();
        var entryCount = await verifyContext.LedgerEntries
            .Where(e => e.IdempotencyKey == "replay-key-1")
            .CountAsync(TestContext.CurrentContext.CancellationToken);
        entryCount.Should().Be(1);
    }

    private async Task<SpendOutcome> SpendSafelyAsync(Guid userId, Guid currencyId, decimal amount, string idempotencyKey)
    {
        var ledgerService = CreateLedgerService(out var dbContext);
        await using var _ = dbContext;

        try
        {
            var result = await ledgerService.SpendAsync(
                new LedgerMutationRequest(userId, currencyId, amount, idempotencyKey),
                TestContext.CurrentContext.CancellationToken);
            return new SpendOutcome(Succeeded: true, InsufficientFunds: false, result.Balance);
        }
        catch (InsufficientFundsException)
        {
            return new SpendOutcome(Succeeded: false, InsufficientFunds: true, Balance: null);
        }
    }

    private sealed record SpendOutcome(bool Succeeded, bool InsufficientFunds, decimal? Balance);

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
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.Currencies.Add(currency);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        return currency.Id;
    }

    private LedgerService CreateLedgerService(out EconomyDbContext dbContext)
    {
        dbContext = CreateDbContext();
        var idempotencyStore = new IdempotencyStore(dbContext);
        var balanceService = new BalanceService(dbContext);
        return new LedgerService(dbContext, idempotencyStore, balanceService, TimeProvider.System);
    }

    private EconomyDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EconomyDbContext>().UseNpgsql(_connectionString);
        return new EconomyDbContext(optionsBuilder.Options);
    }
}
