using System.Collections.Concurrent;
using System.Diagnostics;
using AwesomeAssertions;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Inbox;
using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Testing;
using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Messaging;
using EconomyService.Options;
using EconomyService.Persistence;
using EconomyService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NUnit.Framework;
using OpenTelemetry;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using Respawn;
using Testcontainers.PostgreSql;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace EconomyService.Tests.Integration;

// UserEmailConfirmedConsumer binds to gbp.identity (a real external exchange,
// not self-consumption like DeduplicatingEventConsumer/ConsumerDeduplicationTests)
// and grants a welcome balance through WelcomeGrantService -> ILedgerService. The
// interesting risk here is the nested-transaction avoidance described on
// UserEmailConfirmedConsumer itself: the grant runs in its own scope/
// transaction, separate from the inbox's own processed_messages bookkeeping,
// so both layers of idempotency are exercised and asserted independently.
[TestFixture]
public sealed class WelcomeGrantConsumerTests : IAsyncDisposable
{
    private const string ExchangeName = "gbp.identity";
    private const string RoutingKey = "user.email_confirmed";
    private const string CurrencyCode = "PLATFORM_CREDITS";
    private const decimal GrantAmount = 100m;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(TestContainerImages.Postgres)
        .WithDatabase("economy_db")
        .WithUsername("economy")
        .WithPassword("economy_test_password")
        .Build();

    private string _connectionString = null!;
    private Respawner _respawner = null!;
    private RabbitMqOptions _rabbitMqOptions = null!;
    private Guid _currencyId;

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

        // Publish-side options point at gbp.identity, not gbp.economy - this
        // fixture plays the role of identity-service's own publisher, since
        // no real IdentityService host runs in this test. The consumer under
        // test binds to the same exchange via its own hardcoded exchangeName
        // constant, independent of this options object.
        _rabbitMqOptions = new RabbitMqOptions
        {
            Host = RabbitMqTestBroker.Container.Hostname,
            Port = RabbitMqTestBroker.Container.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
            ExchangeName = ExchangeName,
        };

        await using var topologyConnection = new RabbitMqConnection(MsOptions.Create(_rabbitMqOptions));
        await using var channel = await topologyConnection.CreateChannelAsync(TestContext.CurrentContext.CancellationToken);
        await channel.ExchangeDeclareAsync(
            ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: TestContext.CurrentContext.CancellationToken);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await DisposeAsync();

    [SetUp]
    public async Task SetUpAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);

        // WelcomeGrantService looks the currency up by code, not by a
        // hardcoded id - seed it with a fresh, test-generated Id each run so
        // a passing assertion against that Id later actually proves the
        // lookup, rather than coincidentally matching a literal.
        _currencyId = Guid.CreateVersion7();

        await using var dbContext = CreateDbContext();
        dbContext.Currencies.Add(new Currency
        {
            Id = _currencyId,
            Code = CurrencyCode,
            DisplayName = "Platform Credits",
            Scope = CurrencyScope.Platform,
            GameId = null,
            Decimals = 2,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Test]
    public async Task Deliver_UserEmailConfirmed_GrantsWelcomeBalanceOnce()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var messageId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await using var provider = BuildProvider(new NoOpInboxFaultInjector());
        using var consumer = CreateConsumer(provider, out var queueName);

        await consumer.StartAsync(cancellationToken);
        try
        {
            await WaitForConsumerReadyAsync(queueName, cancellationToken);
            await PublishAsync(messageId, userId, cancellationToken);

            await WaitUntilAsync(
                async () =>
                {
                    await using var verify = CreateDbContext();
                    return await verify.LedgerEntries.AnyAsync(e => e.UserId == userId, cancellationToken);
                },
                TimeSpan.FromSeconds(15));
        }
        finally
        {
            await consumer.StopAsync(cancellationToken);
        }

        await using var final = CreateDbContext();

        var entry = await final.LedgerEntries.SingleAsync(e => e.UserId == userId, cancellationToken);
        entry.IdempotencyKey.Should().Be($"welcome:{userId}");
        entry.Amount.Should().Be(GrantAmount);
        // Proves the currency was resolved by configured code, not a
        // hardcoded id - _currencyId is generated fresh per test run.
        entry.CurrencyId.Should().Be(_currencyId);

        var balance = await final.Balances.SingleAsync(b => b.UserId == userId && b.CurrencyId == _currencyId, cancellationToken);
        balance.Amount.Should().Be(GrantAmount);
    }

    // UserEmailConfirmedConsumer already parses payload.UserId for the grant itself - tagging the
    // consumer activity (started in InboxConsumerBase.HandleDeliveryAsync) with enduser.id reuses
    // that same value rather than adding a new field anywhere.
    [Test]
    public async Task Deliver_UserEmailConfirmed_TagsConsumerActivityWithUserId()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var messageId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        // Marked Recorded explicitly - an unmarked legacy Activity defaults to "00" (unsampled) trace
        // flags, which would make the default ParentBasedSampler correctly, but misleadingly for this
        // test, decline to sample the consumer activity below.
        using var seedActivity = new Activity("seed-publish").Start();
        seedActivity.ActivityTraceFlags = ActivityTraceFlags.Recorded;

        // Propagators.DefaultTextMapPropagator is a Noop until a real TracerProvider has been built
        // at least once in the process - without it, Extract silently fails to read the traceparent
        // header back out, and the consumer activity would always root fresh instead of parenting.
        using var tracerProvider = Sdk.CreateTracerProviderBuilder().AddSource("BuildingBlocks.Messaging").Build();

        var capturedActivities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "BuildingBlocks.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = capturedActivities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var provider = BuildProvider(new NoOpInboxFaultInjector());
        using var consumer = CreateConsumer(provider, out var queueName);

        await consumer.StartAsync(cancellationToken);
        try
        {
            await WaitForConsumerReadyAsync(queueName, cancellationToken);
            await PublishWithTraceParentAsync(messageId, userId, seedActivity.Id!, cancellationToken);

            await WaitUntilAsync(
                async () =>
                {
                    await using var verify = CreateDbContext();
                    return await verify.LedgerEntries.AnyAsync(e => e.UserId == userId, cancellationToken);
                },
                TimeSpan.FromSeconds(15));

            await WaitUntilAsync(
                () => Task.FromResult(capturedActivities.Any(a => a.TraceId == seedActivity.TraceId)),
                TimeSpan.FromSeconds(10));
        }
        finally
        {
            await consumer.StopAsync(cancellationToken);
        }

        var consumerActivity = capturedActivities.Single(a => a.TraceId == seedActivity.TraceId);
        consumerActivity.TagObjects.Should().Contain(t => t.Key == "enduser.id" && Equals(t.Value, userId));
    }

    [Test]
    public async Task Deliver_SameMessageTwice_DoesNotCreateSecondLedgerEntry()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var messageId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await using var provider = BuildProvider(new NoOpInboxFaultInjector());
        using var consumer = CreateConsumer(provider, out var queueName);

        await consumer.StartAsync(cancellationToken);
        try
        {
            await WaitForConsumerReadyAsync(queueName, cancellationToken);
            await PublishAsync(messageId, userId, cancellationToken);
            await PublishAsync(messageId, userId, cancellationToken);

            await WaitUntilAsync(
                async () =>
                {
                    await using var verify = CreateDbContext();
                    return await verify.LedgerEntries.AnyAsync(e => e.UserId == userId, cancellationToken);
                },
                TimeSpan.FromSeconds(15));

            // The second delivery is processed (acked or skipped) shortly
            // after the first; give it room to land before asserting counts.
            await AssertQueueEmptyAsync(queueName, cancellationToken, TimeSpan.FromSeconds(10));
        }
        finally
        {
            await consumer.StopAsync(cancellationToken);
        }

        await using var final = CreateDbContext();

        // Both idempotency layers checked separately, not just "one row in
        // total": the inbox's own processed_messages dedup...
        var processedCount = await final.ProcessedMessages.CountAsync(m => m.MessageId == messageId, cancellationToken);
        processedCount.Should().Be(1);

        // ...and, independently, the ledger's own idempotency_key - this is
        // the one that actually stops a double grant if it were ever reached
        // twice, and it's what A.4 is really about.
        var ledgerCount = await final.LedgerEntries.CountAsync(e => e.UserId == userId, cancellationToken);
        ledgerCount.Should().Be(1);
    }

    [Test]
    public async Task Deliver_CrashBetweenSideEffectAndCommit_RedeliveryGrantsExactlyOnce()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var messageId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var faultInjector = new ThrowOnceInboxFaultInjector();

        await using var provider = BuildProvider(faultInjector);
        using var consumer = CreateConsumer(provider, out var queueName);

        await consumer.StartAsync(cancellationToken);
        try
        {
            await WaitForConsumerReadyAsync(queueName, cancellationToken);
            await PublishAsync(messageId, userId, cancellationToken);

            await WaitUntilAsync(
                async () =>
                {
                    await using var verify = CreateDbContext();
                    return await verify.ProcessedMessages.AnyAsync(m => m.MessageId == messageId, cancellationToken);
                },
                TimeSpan.FromSeconds(15));
        }
        finally
        {
            await consumer.StopAsync(cancellationToken);
        }

        faultInjector.Invocations.Should().BeGreaterThanOrEqualTo(2, "the first delivery must have been rolled back and redelivered");

        await using var final = CreateDbContext();

        // The grant itself already committed on the first attempt (it runs
        // in its own scope/transaction, independent of the inbox's), before
        // the injected fault rolled back the inbox's processed_messages
        // insert. Redelivery re-runs WelcomeGrantService with the same
        // idempotency key and replays instead of posting a second entry.
        var processedCount = await final.ProcessedMessages.CountAsync(m => m.MessageId == messageId, cancellationToken);
        processedCount.Should().Be(1);

        var ledgerCount = await final.LedgerEntries.CountAsync(e => e.UserId == userId, cancellationToken);
        ledgerCount.Should().Be(1);
    }

    private async Task PublishAsync(Guid messageId, Guid userId, CancellationToken cancellationToken)
    {
        var payload = $$"""{"Id":"{{messageId}}","OccurredAt":"2026-07-19T00:00:00+00:00","UserId":"{{userId}}"}""";

        await using var connection = new RabbitMqConnection(MsOptions.Create(_rabbitMqOptions));
        var eventBus = new RabbitMqEventBus(connection, MsOptions.Create(_rabbitMqOptions));
        await eventBus.PublishAsync(new EventEnvelope(RoutingKey, 1, payload), cancellationToken: cancellationToken);
    }

    // Simulates what the outbox dispatcher's PublishOneAsync actually sends: the same payload shape
    // as PublishAsync above, plus the W3C traceparent header a real Producer activity would inject.
    private async Task PublishWithTraceParentAsync(Guid messageId, Guid userId, string traceParent, CancellationToken cancellationToken)
    {
        var payload = $$"""{"Id":"{{messageId}}","OccurredAt":"2026-07-19T00:00:00+00:00","UserId":"{{userId}}"}""";

        await using var connection = new RabbitMqConnection(MsOptions.Create(_rabbitMqOptions));
        var eventBus = new RabbitMqEventBus(connection, MsOptions.Create(_rabbitMqOptions));
        await eventBus.PublishAsync(
            new EventEnvelope(RoutingKey, 1, payload),
            headers: new Dictionary<string, string> { ["traceparent"] = traceParent },
            cancellationToken);
    }

    // BackgroundService.StartAsync returns once ExecuteAsync has been
    // scheduled, not once the queue declare/bind inside it has actually run
    // - publishing before that finishes would route into an exchange with no
    // bound queue yet and lose the message. Poll for the queue to exist
    // before a test relies on anything published reaching it.
    private async Task WaitForConsumerReadyAsync(string queueName, CancellationToken cancellationToken) =>
        await WaitUntilAsync(
            async () =>
            {
                try
                {
                    await using var connection = new RabbitMqConnection(MsOptions.Create(_rabbitMqOptions));
                    await using var channel = await connection.CreateChannelAsync(cancellationToken);
                    await channel.QueueDeclarePassiveAsync(queueName, cancellationToken);
                    return true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return false;
                }
            },
            TimeSpan.FromSeconds(15));

    private async Task AssertQueueEmptyAsync(string queueName, CancellationToken cancellationToken, TimeSpan? settleFor = null)
    {
        if (settleFor is { } delay)
        {
            await Task.Delay(delay, cancellationToken);
        }

        await using var connection = new RabbitMqConnection(MsOptions.Create(_rabbitMqOptions));
        await using var channel = await connection.CreateChannelAsync(cancellationToken);
        var declareOk = await channel.QueueDeclarePassiveAsync(queueName, cancellationToken);

        declareOk.MessageCount.Should().Be(0u);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);
        }

        Assert.Fail($"Condition was not met within {timeout}.");
    }

    private UserEmailConfirmedConsumer CreateConsumer(ServiceProvider provider, out string queueName)
    {
        queueName = $"gbp.economy.welcome-grant.test.{Guid.NewGuid():N}";

        return new UserEmailConfirmedConsumer(
            new RabbitMqConnection(MsOptions.Create(_rabbitMqOptions)),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IInboxFaultInjector>(),
            TimeProvider.System,
            NullLogger<UserEmailConfirmedConsumer>.Instance,
            queueName);
    }

    private ServiceProvider BuildProvider(IInboxFaultInjector faultInjector)
    {
        var services = new ServiceCollection();
        services.AddDbContext<EconomyDbContext>(o => o.UseNpgsql(_connectionString));
        services.AddSingleton(faultInjector);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IBalanceService, BalanceService>();
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<IOutboxWriter, OutboxWriter<EconomyDbContext>>();
        services.AddScoped<IWelcomeGrantService, WelcomeGrantService>();
        services.AddOptions<WelcomeGrantOptions>().Configure(o =>
        {
            o.Amount = GrantAmount;
            o.CurrencyCode = CurrencyCode;
        });
        return services.BuildServiceProvider();
    }

    private EconomyDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EconomyDbContext>().UseNpgsql(_connectionString);
        return new EconomyDbContext(optionsBuilder.Options);
    }

    // Throws on the first invocation only - simulating a crash between the
    // side effect and the commit - then behaves like the no-op on every
    // later call, so the redelivered message succeeds instead of looping
    // forever against an always-failing seam.
    private sealed class ThrowOnceInboxFaultInjector : IInboxFaultInjector
    {
        private int _invocations;

        public int Invocations => _invocations;

        public Task BeforeCommitAsync(Guid messageId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _invocations) == 1)
            {
                throw new InvalidOperationException("injected crash between side effect and commit");
            }

            return Task.CompletedTask;
        }
    }
}
