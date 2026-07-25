using AwesomeAssertions;
using BuildingBlocks.Messaging.Outbox;
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
public sealed class ConversionSagaTests : IAsyncDisposable
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
    public async Task ExecuteAsync_HappyPath_DebitsPlatformCreditsGameAndCompletes()
    {
        var platformCurrencyId = await SeedPlatformCurrencyAsync();
        var (gameCurrencyId, gameId) = await SeedGameCurrencyAsync();
        var userId = Guid.NewGuid();
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        await GrantAsync(userId, platformCurrencyId, 100m, "conversion-saga-seed-1");
        var request = await SeedConversionRequestAsync(userId, platformCurrencyId, gameCurrencyId, gameId, 10m, 1000m, 100m);

        await using var dbContext = CreateDbContext();
        var saga = CreateSaga(dbContext);
        await saga.ExecuteAsync(request.Id, cancellationToken);

        await using var verifyContext = CreateDbContext();

        var platformBalance = await verifyContext.Balances
            .SingleAsync(b => b.UserId == userId && b.CurrencyId == platformCurrencyId, cancellationToken);
        platformBalance.Amount.Should().Be(90m);

        var gameBalance = await verifyContext.Balances
            .SingleAsync(b => b.UserId == userId && b.CurrencyId == gameCurrencyId, cancellationToken);
        gameBalance.Amount.Should().Be(1000m);

        var conversionOut = await verifyContext.LedgerEntries.SingleAsync(
            e => e.UserId == userId && e.CurrencyId == platformCurrencyId && e.TransactionType == TransactionType.ConversionOut,
            cancellationToken);
        conversionOut.Amount.Should().Be(-10m);

        var conversionIn = await verifyContext.LedgerEntries.SingleAsync(
            e => e.UserId == userId && e.CurrencyId == gameCurrencyId && e.TransactionType == TransactionType.ConversionIn,
            cancellationToken);
        conversionIn.Amount.Should().Be(1000m);

        var updatedRequest = await verifyContext.ConversionRequests.SingleAsync(r => r.Id == request.Id, cancellationToken);
        updatedRequest.Status.Should().Be(ConversionStatus.Completed);

        (await verifyContext.OutboxMessages.AnyAsync(m => m.Type == "conversion.debited", cancellationToken)).Should().BeTrue();
        (await verifyContext.OutboxMessages.AnyAsync(m => m.Type == "conversion.completed", cancellationToken)).Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_CreditStepFails_CompensatesAndRestoresPlatformBalance()
    {
        var platformCurrencyId = await SeedPlatformCurrencyAsync();
        var (gameCurrencyId, gameId) = await SeedGameCurrencyAsync();
        var userId = Guid.NewGuid();
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        await GrantAsync(userId, platformCurrencyId, 100m, "conversion-saga-seed-2");
        var request = await SeedConversionRequestAsync(userId, platformCurrencyId, gameCurrencyId, gameId, 10m, 1000m, 100m);

        await using var dbContext = CreateDbContext();
        var saga = CreateSaga(dbContext, new ThrowingConversionCreditFaultInjector());
        await saga.ExecuteAsync(request.Id, cancellationToken);

        await using var verifyContext = CreateDbContext();

        var platformBalance = await verifyContext.Balances
            .SingleAsync(b => b.UserId == userId && b.CurrencyId == platformCurrencyId, cancellationToken);
        platformBalance.Amount.Should().Be(100m);

        (await verifyContext.Balances.AnyAsync(b => b.UserId == userId && b.CurrencyId == gameCurrencyId, cancellationToken))
            .Should().BeFalse();

        var updatedRequest = await verifyContext.ConversionRequests.SingleAsync(r => r.Id == request.Id, cancellationToken);
        updatedRequest.Status.Should().Be(ConversionStatus.Failed);
        updatedRequest.FailureReason.Should().NotBeNullOrWhiteSpace();

        var compensationEntry = await verifyContext.LedgerEntries.SingleAsync(
            e => e.UserId == userId
                && e.CurrencyId == platformCurrencyId
                && e.TransactionType == TransactionType.Grant
                && e.Reason == "conversion compensation",
            cancellationToken);
        compensationEntry.Amount.Should().Be(10m);

        var conversionOutCount = await verifyContext.LedgerEntries.CountAsync(
            e => e.CurrencyId == platformCurrencyId && e.TransactionType == TransactionType.ConversionOut, cancellationToken);
        conversionOutCount.Should().Be(1);

        (await verifyContext.LedgerEntries.AnyAsync(e => e.TransactionType == TransactionType.ConversionIn, cancellationToken))
            .Should().BeFalse();

        (await verifyContext.OutboxMessages.AnyAsync(m => m.Type == "conversion.failed", cancellationToken)).Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_CalledAgainAfterCompleted_DoesNotPostSecondPairOfEntries()
    {
        var platformCurrencyId = await SeedPlatformCurrencyAsync();
        var (gameCurrencyId, gameId) = await SeedGameCurrencyAsync();
        var userId = Guid.NewGuid();
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        await GrantAsync(userId, platformCurrencyId, 100m, "conversion-saga-seed-3");
        var request = await SeedConversionRequestAsync(userId, platformCurrencyId, gameCurrencyId, gameId, 10m, 1000m, 100m);

        await using (var firstRunContext = CreateDbContext())
        {
            await CreateSaga(firstRunContext).ExecuteAsync(request.Id, cancellationToken);
        }

        await using (var secondRunContext = CreateDbContext())
        {
            await CreateSaga(secondRunContext).ExecuteAsync(request.Id, cancellationToken);
        }

        await using var verifyContext = CreateDbContext();

        var platformBalance = await verifyContext.Balances
            .SingleAsync(b => b.UserId == userId && b.CurrencyId == platformCurrencyId, cancellationToken);
        platformBalance.Amount.Should().Be(90m);

        var conversionOutCount = await verifyContext.LedgerEntries.CountAsync(
            e => e.CurrencyId == platformCurrencyId && e.TransactionType == TransactionType.ConversionOut, cancellationToken);
        conversionOutCount.Should().Be(1);

        var conversionInCount = await verifyContext.LedgerEntries.CountAsync(
            e => e.CurrencyId == gameCurrencyId && e.TransactionType == TransactionType.ConversionIn, cancellationToken);
        conversionInCount.Should().Be(1);

        var updatedRequest = await verifyContext.ConversionRequests.SingleAsync(r => r.Id == request.Id, cancellationToken);
        updatedRequest.Status.Should().Be(ConversionStatus.Completed);
    }

    [Test]
    public async Task ExecuteAsync_CalledAgainAfterFailed_DoesNotCompensateAgain()
    {
        var platformCurrencyId = await SeedPlatformCurrencyAsync();
        var (gameCurrencyId, gameId) = await SeedGameCurrencyAsync();
        var userId = Guid.NewGuid();
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        await GrantAsync(userId, platformCurrencyId, 100m, "conversion-saga-seed-4");
        var request = await SeedConversionRequestAsync(userId, platformCurrencyId, gameCurrencyId, gameId, 10m, 1000m, 100m);

        await using (var firstRunContext = CreateDbContext())
        {
            await CreateSaga(firstRunContext, new ThrowingConversionCreditFaultInjector())
                .ExecuteAsync(request.Id, cancellationToken);
        }

        await using (var secondRunContext = CreateDbContext())
        {
            await CreateSaga(secondRunContext, new ThrowingConversionCreditFaultInjector())
                .ExecuteAsync(request.Id, cancellationToken);
        }

        await using var verifyContext = CreateDbContext();

        var platformBalance = await verifyContext.Balances
            .SingleAsync(b => b.UserId == userId && b.CurrencyId == platformCurrencyId, cancellationToken);
        platformBalance.Amount.Should().Be(100m);

        var compensationCount = await verifyContext.LedgerEntries.CountAsync(
            e => e.CurrencyId == platformCurrencyId && e.TransactionType == TransactionType.Grant && e.Reason == "conversion compensation",
            cancellationToken);
        compensationCount.Should().Be(1);

        var updatedRequest = await verifyContext.ConversionRequests.SingleAsync(r => r.Id == request.Id, cancellationToken);
        updatedRequest.Status.Should().Be(ConversionStatus.Failed);
    }

    [Test]
    public async Task ExecuteAsync_RacesAgainstConcurrentStatusTransition_NeverLeavesDebitUncompensated()
    {
        var platformCurrencyId = await SeedPlatformCurrencyAsync();
        var (gameCurrencyId, gameId) = await SeedGameCurrencyAsync();
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        for (var iteration = 0; iteration < 5; iteration++)
        {
            var userId = Guid.NewGuid();
            await GrantAsync(userId, platformCurrencyId, 100m, $"conversion-race-seed-{iteration}");
            var request = await SeedConversionRequestAsync(userId, platformCurrencyId, gameCurrencyId, gameId, 10m, 1000m, 100m);

            await using var runnerContext = CreateDbContext();
            await using var racerContext = CreateDbContext();
            var runnerSaga = CreateSaga(runnerContext);
            var racerSaga = CreateSaga(racerContext);

            // The racer stands in for the cancellation endpoint added in the
            // next commit: an independent writer attempting the same
            // Started -> Failed transition the runner's own debit step is
            // racing towards Started -> DebitDone.
            var runnerTask = runnerSaga.ExecuteAsync(request.Id, cancellationToken);
            var racerTask = racerSaga.TryTransitionAsync(
                request.Id,
                ConversionStatus.Started,
                setters => setters
                    .SetProperty(r => r.Status, ConversionStatus.Failed)
                    .SetProperty(r => r.FailureReason, "external cancel simulated")
                    .SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);

            await Task.WhenAll(runnerTask, racerTask);

            await using var verifyContext = CreateDbContext();
            var updatedRequest = await verifyContext.ConversionRequests.SingleAsync(r => r.Id == request.Id, cancellationToken);
            updatedRequest.Status.Should().BeOneOf(ConversionStatus.Completed, ConversionStatus.Failed);

            var platformBalance = await verifyContext.Balances
                .SingleAsync(b => b.UserId == userId && b.CurrencyId == platformCurrencyId, cancellationToken);
            var conversionOutCount = await verifyContext.LedgerEntries.CountAsync(
                e => e.UserId == userId && e.CurrencyId == platformCurrencyId && e.TransactionType == TransactionType.ConversionOut,
                cancellationToken);
            var conversionInCount = await verifyContext.LedgerEntries.CountAsync(
                e => e.UserId == userId && e.CurrencyId == gameCurrencyId && e.TransactionType == TransactionType.ConversionIn,
                cancellationToken);

            if (updatedRequest.Status == ConversionStatus.Completed)
            {
                platformBalance.Amount.Should().Be(90m);
                conversionOutCount.Should().Be(1);
                conversionInCount.Should().Be(1);
            }
            else
            {
                // The racer won - debit must never have landed, since it was
                // still guarded by the Started status the racer just took.
                platformBalance.Amount.Should().Be(100m);
                conversionOutCount.Should().Be(0);
                conversionInCount.Should().Be(0);
            }
        }
    }

    [Test]
    public async Task TryCancelAsync_Started_MarksFailedAndPostsNoDebit()
    {
        var platformCurrencyId = await SeedPlatformCurrencyAsync();
        var (gameCurrencyId, gameId) = await SeedGameCurrencyAsync();
        var userId = Guid.NewGuid();
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        await GrantAsync(userId, platformCurrencyId, 100m, "conversion-cancel-seed-1");
        var request = await SeedConversionRequestAsync(userId, platformCurrencyId, gameCurrencyId, gameId, 10m, 1000m, 100m);

        await using var dbContext = CreateDbContext();
        await CreateSaga(dbContext).TryCancelAsync(request.Id, cancellationToken);

        await using var verifyContext = CreateDbContext();
        var updatedRequest = await verifyContext.ConversionRequests.SingleAsync(r => r.Id == request.Id, cancellationToken);
        updatedRequest.Status.Should().Be(ConversionStatus.Failed);
        updatedRequest.FailureReason.Should().Be("cancelled by user");

        var platformBalance = await verifyContext.Balances
            .SingleAsync(b => b.UserId == userId && b.CurrencyId == platformCurrencyId, cancellationToken);
        platformBalance.Amount.Should().Be(100m);

        (await verifyContext.LedgerEntries.AnyAsync(
                e => e.CurrencyId == platformCurrencyId && e.TransactionType == TransactionType.ConversionOut, cancellationToken))
            .Should().BeFalse();

        (await verifyContext.OutboxMessages.AnyAsync(m => m.Type == "conversion.failed", cancellationToken)).Should().BeTrue();
    }

    [Test]
    public async Task TryCancelAsync_DebitDone_CompensatesAndRestoresBalance()
    {
        var platformCurrencyId = await SeedPlatformCurrencyAsync();
        var (gameCurrencyId, gameId) = await SeedGameCurrencyAsync();
        var userId = Guid.NewGuid();
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        await GrantAsync(userId, platformCurrencyId, 100m, "conversion-cancel-seed-2");
        var request = await SeedDebitDoneConversionAsync(userId, platformCurrencyId, gameCurrencyId, gameId, 10m, 1000m, 100m);

        await using var dbContext = CreateDbContext();
        await CreateSaga(dbContext).TryCancelAsync(request.Id, cancellationToken);

        await using var verifyContext = CreateDbContext();
        var updatedRequest = await verifyContext.ConversionRequests.SingleAsync(r => r.Id == request.Id, cancellationToken);
        updatedRequest.Status.Should().Be(ConversionStatus.Failed);
        updatedRequest.FailureReason.Should().Be("cancelled by user");

        var platformBalance = await verifyContext.Balances
            .SingleAsync(b => b.UserId == userId && b.CurrencyId == platformCurrencyId, cancellationToken);
        platformBalance.Amount.Should().Be(100m);

        (await verifyContext.Balances.AnyAsync(b => b.UserId == userId && b.CurrencyId == gameCurrencyId, cancellationToken))
            .Should().BeFalse();

        (await verifyContext.OutboxMessages.AnyAsync(m => m.Type == "conversion.failed", cancellationToken)).Should().BeTrue();
    }

    [TestCase(ConversionStatus.Completed)]
    [TestCase(ConversionStatus.Failed)]
    [TestCase(ConversionStatus.Compensating)]
    public async Task TryCancelAsync_TerminalOrCompensatingStatus_ThrowsConversionNotCancellable(ConversionStatus status)
    {
        var platformCurrencyId = await SeedPlatformCurrencyAsync();
        var (gameCurrencyId, gameId) = await SeedGameCurrencyAsync();
        var userId = Guid.NewGuid();
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        var request = await SeedConversionRequestAsync(
            userId, platformCurrencyId, gameCurrencyId, gameId, 10m, 1000m, 100m, status);

        await using var dbContext = CreateDbContext();
        var act = async () => await CreateSaga(dbContext).TryCancelAsync(request.Id, cancellationToken);

        await act.Should().ThrowAsync<ConversionNotCancellableException>();
    }

    [Test]
    public async Task TryCancelAsync_RacesAgainstExecuteAsync_NeverLeavesDebitUncompensated()
    {
        var platformCurrencyId = await SeedPlatformCurrencyAsync();
        var (gameCurrencyId, gameId) = await SeedGameCurrencyAsync();
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        for (var iteration = 0; iteration < 5; iteration++)
        {
            var userId = Guid.NewGuid();
            await GrantAsync(userId, platformCurrencyId, 100m, $"conversion-cancel-race-seed-{iteration}");
            var request = await SeedConversionRequestAsync(userId, platformCurrencyId, gameCurrencyId, gameId, 10m, 1000m, 100m);

            await using var runnerContext = CreateDbContext();
            await using var cancelContext = CreateDbContext();
            var runnerTask = CreateSaga(runnerContext).ExecuteAsync(request.Id, cancellationToken);
            var cancelTask = CancelIgnoringTerminalConflictAsync(CreateSaga(cancelContext), request.Id, cancellationToken);

            await Task.WhenAll(runnerTask, cancelTask);

            await using var verifyContext = CreateDbContext();
            var updatedRequest = await verifyContext.ConversionRequests.SingleAsync(r => r.Id == request.Id, cancellationToken);
            updatedRequest.Status.Should().BeOneOf(ConversionStatus.Completed, ConversionStatus.Failed);

            var platformBalance = await verifyContext.Balances
                .SingleAsync(b => b.UserId == userId && b.CurrencyId == platformCurrencyId, cancellationToken);
            var conversionOutCount = await verifyContext.LedgerEntries.CountAsync(
                e => e.UserId == userId && e.CurrencyId == platformCurrencyId && e.TransactionType == TransactionType.ConversionOut,
                cancellationToken);
            var conversionInCount = await verifyContext.LedgerEntries.CountAsync(
                e => e.UserId == userId && e.CurrencyId == gameCurrencyId && e.TransactionType == TransactionType.ConversionIn,
                cancellationToken);
            var compensationCount = await verifyContext.LedgerEntries.CountAsync(
                e => e.UserId == userId && e.CurrencyId == platformCurrencyId && e.TransactionType == TransactionType.Grant
                    && e.Reason == "conversion compensation",
                cancellationToken);

            if (updatedRequest.Status == ConversionStatus.Completed)
            {
                platformBalance.Amount.Should().Be(90m);
                conversionOutCount.Should().Be(1);
                conversionInCount.Should().Be(1);
                compensationCount.Should().Be(0);
            }
            else
            {
                // Never "debited without a matching compensation" - either
                // the debit never landed at all, or it landed and was paid
                // back in full, but never one without the other.
                platformBalance.Amount.Should().Be(100m);
                conversionInCount.Should().Be(0);
                conversionOutCount.Should().Be(compensationCount);
                conversionOutCount.Should().BeLessThanOrEqualTo(1);
            }
        }
    }

    private static async Task CancelIgnoringTerminalConflictAsync(ConversionSaga saga, Guid conversionId, CancellationToken cancellationToken)
    {
        try
        {
            await saga.TryCancelAsync(conversionId, cancellationToken);
        }
        catch (ConversionNotCancellableException)
        {
            // The runner reached Completed before cancel's very first read -
            // a valid race outcome, not a test failure.
        }
    }

    private static ConversionSaga CreateSaga(EconomyDbContext dbContext, IConversionCreditFaultInjector? faultInjector = null)
    {
        var idempotencyStore = new IdempotencyStore(dbContext);
        var balanceService = new BalanceService(dbContext);
        var outboxWriter = new OutboxWriter<EconomyDbContext>(dbContext);
        var ledgerService = new LedgerService(dbContext, idempotencyStore, balanceService, outboxWriter, TimeProvider.System);

        return new ConversionSaga(
            dbContext, ledgerService, outboxWriter, faultInjector ?? new NoOpConversionCreditFaultInjector(), TimeProvider.System);
    }

    private async Task GrantAsync(Guid userId, Guid currencyId, decimal amount, string idempotencyKey)
    {
        await using var dbContext = CreateDbContext();
        var idempotencyStore = new IdempotencyStore(dbContext);
        var balanceService = new BalanceService(dbContext);
        var outboxWriter = new OutboxWriter<EconomyDbContext>(dbContext);
        var ledgerService = new LedgerService(dbContext, idempotencyStore, balanceService, outboxWriter, TimeProvider.System);

        await ledgerService.GrantAsync(
            new LedgerMutationRequest(userId, currencyId, amount, idempotencyKey), TestContext.CurrentContext.CancellationToken);
    }

    private async Task<ConversionRequest> SeedConversionRequestAsync(
        Guid userId, Guid fromCurrencyId, Guid toCurrencyId, Guid? gameId, decimal fromAmount, decimal toAmount, decimal rate,
        ConversionStatus status = ConversionStatus.Started)
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var request = new ConversionRequest
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FromCurrencyId = fromCurrencyId,
            ToCurrencyId = toCurrencyId,
            GameId = gameId,
            FromAmount = fromAmount,
            ToAmount = toAmount,
            RateApplied = rate,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.ConversionRequests.Add(request);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        return request;
    }

    // Posts the debit leg directly against the ledger and marks the row
    // DebitDone, without running the saga - a deterministic way to land a
    // conversion exactly at DebitDone for tests that need to cancel from
    // there, rather than racing the real saga to catch it mid-flight.
    private async Task<ConversionRequest> SeedDebitDoneConversionAsync(
        Guid userId, Guid fromCurrencyId, Guid toCurrencyId, Guid? gameId, decimal fromAmount, decimal toAmount, decimal rate)
    {
        var request = await SeedConversionRequestAsync(userId, fromCurrencyId, toCurrencyId, gameId, fromAmount, toAmount, rate);

        await using var dbContext = CreateDbContext();
        var idempotencyStore = new IdempotencyStore(dbContext);
        var balanceService = new BalanceService(dbContext);
        var outboxWriter = new OutboxWriter<EconomyDbContext>(dbContext);
        var ledgerService = new LedgerService(dbContext, idempotencyStore, balanceService, outboxWriter, TimeProvider.System);

        await ledgerService.ConversionDebitAsync(
            new LedgerMutationRequest(userId, fromCurrencyId, fromAmount, $"conversion:{request.Id}:debit", "conversion debit"),
            (_, ct) => dbContext.ConversionRequests
                .Where(r => r.Id == request.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.Status, ConversionStatus.DebitDone), ct),
            TestContext.CurrentContext.CancellationToken);

        return request;
    }

    private async Task<Guid> SeedPlatformCurrencyAsync()
    {
        await using var dbContext = CreateDbContext();
        var currency = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = $"PLATFORM_{Guid.NewGuid():N}",
            DisplayName = "Test Platform Credits",
            Scope = CurrencyScope.Platform,
            GameId = null,
            Decimals = 2,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.Currencies.Add(currency);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        return currency.Id;
    }

    private async Task<(Guid CurrencyId, Guid GameId)> SeedGameCurrencyAsync()
    {
        await using var dbContext = CreateDbContext();
        var gameId = Guid.NewGuid();
        var currency = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = $"GAME_{Guid.NewGuid():N}",
            DisplayName = "Test Game Gold",
            Scope = CurrencyScope.Game,
            GameId = gameId,
            Decimals = 2,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.Currencies.Add(currency);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        return (currency.Id, gameId);
    }

    private EconomyDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EconomyDbContext>().UseNpgsql(_connectionString);
        return new EconomyDbContext(optionsBuilder.Options);
    }

    // A deterministic fault at the credit step - not a database mock, a
    // one-line stand-in for "the credit step failed" so the compensating
    // path is a straightforward sequential call to test, not a race against
    // a timing-dependent failure.
    private sealed class ThrowingConversionCreditFaultInjector : IConversionCreditFaultInjector
    {
        public Task BeforeCreditAsync(ConversionRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("injected credit failure");
    }
}
