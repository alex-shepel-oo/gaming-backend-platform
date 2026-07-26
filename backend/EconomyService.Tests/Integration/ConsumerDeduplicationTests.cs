using AwesomeAssertions;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Inbox;
using EconomyService.Messaging;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NUnit.Framework;
using RabbitMQ.Client;
using Respawn;
using Testcontainers.PostgreSql;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace EconomyService.Tests.Integration;

[TestFixture]
public sealed class ConsumerDeduplicationTests : IAsyncDisposable
{
    private const string RoutingKey = "balance.changed";

    // A second exchange, entirely separate from _rabbitMqOptions.ExchangeName
    // (gbp.economy) - exists only to prove a consumer can bind to an
    // exchange it was handed explicitly rather than the one on its own
    // publish-side options (A.3).
    private const string OtherExchangeName = "gbp.test-other";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("economy_db")
        .WithUsername("economy")
        .WithPassword("economy_test_password")
        .Build();

    private string _connectionString = null!;
    private Respawner _respawner = null!;
    private RabbitMqOptions _rabbitMqOptions = null!;

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

        _rabbitMqOptions = new RabbitMqOptions
        {
            Host = RabbitMqTestBroker.Container.Hostname,
            Port = RabbitMqTestBroker.Container.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
        };

        await using var topologyConnection = new RabbitMqConnection(MsOptions.Create(_rabbitMqOptions));
        await using var channel = await topologyConnection.CreateChannelAsync(TestContext.CurrentContext.CancellationToken);
        await channel.ExchangeDeclareAsync(
            _rabbitMqOptions.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: TestContext.CurrentContext.CancellationToken);

        await channel.ExchangeDeclareAsync(
            OtherExchangeName,
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

        // Each test gets its own queue name (see CreateConsumer) so a
        // leftover binding or in-flight message from a previous test can
        // never leak into the next one via the shared exchange.
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Test]
    public async Task Deliver_NewMessage_WritesProcessedMessageAndAppliesSideEffectOnce()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var messageId = Guid.CreateVersion7();

        await using var provider = BuildProvider(new NoOpInboxFaultInjector());
        using var consumer = CreateConsumer(provider, out var queueName);

        await consumer.StartAsync(cancellationToken);
        try
        {
            await WaitForConsumerReadyAsync(queueName, cancellationToken);
            await PublishAsync(messageId, cancellationToken);

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

        await using var final = CreateDbContext();
        var processed = await final.ProcessedMessages.SingleAsync(m => m.MessageId == messageId, cancellationToken);
        processed.ProcessedAt.Should().NotBe(default);

        var count = await final.ProjectedEventCounts.SingleAsync(c => c.EventType == RoutingKey, cancellationToken);
        count.Count.Should().Be(1);

        await AssertQueueEmptyAsync(queueName, cancellationToken);
    }

    [Test]
    public async Task Deliver_SameMessageTwice_AppliesSideEffectOnceAndAcksBothDeliveries()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var messageId = Guid.CreateVersion7();

        await using var provider = BuildProvider(new NoOpInboxFaultInjector());
        using var consumer = CreateConsumer(provider, out var queueName);

        await consumer.StartAsync(cancellationToken);
        try
        {
            await WaitForConsumerReadyAsync(queueName, cancellationToken);
            await PublishAsync(messageId, cancellationToken);
            await PublishAsync(messageId, cancellationToken);

            await WaitUntilAsync(
                async () =>
                {
                    await using var verify = CreateDbContext();
                    var projected = await verify.ProjectedEventCounts.SingleOrDefaultAsync(c => c.EventType == RoutingKey, cancellationToken);
                    return projected is not null;
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
        var processedCount = await final.ProcessedMessages.CountAsync(m => m.MessageId == messageId, cancellationToken);
        processedCount.Should().Be(1);

        var projectedCount = await final.ProjectedEventCounts.SingleAsync(c => c.EventType == RoutingKey, cancellationToken);
        projectedCount.Count.Should().Be(1);
    }

    [Test]
    public async Task Deliver_CrashBetweenSideEffectAndCommit_RedeliveryReprocessesSuccessfully()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var messageId = Guid.CreateVersion7();
        var faultInjector = new ThrowOnceInboxFaultInjector();

        await using var provider = BuildProvider(faultInjector);
        using var consumer = CreateConsumer(provider, out var queueName);

        await consumer.StartAsync(cancellationToken);
        try
        {
            await WaitForConsumerReadyAsync(queueName, cancellationToken);
            await PublishAsync(messageId, cancellationToken);

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
        var processedCount = await final.ProcessedMessages.CountAsync(m => m.MessageId == messageId, cancellationToken);
        processedCount.Should().Be(1);

        var projectedCount = await final.ProjectedEventCounts.SingleAsync(c => c.EventType == RoutingKey, cancellationToken);
        projectedCount.Count.Should().Be(1);
    }

    [Test]
    public async Task Deliver_MessageOnExplicitExchange_ConsumerBoundToThatExchangeReceivesItNotOptionsExchange()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var messageId = Guid.CreateVersion7();

        await using var provider = BuildProvider(new NoOpInboxFaultInjector());
        using var consumer = CreateOtherExchangeConsumer(provider, out var queueName);

        await consumer.StartAsync(cancellationToken);
        try
        {
            await WaitForConsumerReadyAsync(queueName, cancellationToken);
            await PublishToOtherExchangeAsync(messageId, cancellationToken);

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

        await using var final = CreateDbContext();
        var processed = await final.ProcessedMessages.SingleAsync(m => m.MessageId == messageId, cancellationToken);
        processed.ProcessedAt.Should().NotBe(default);

        var count = await final.ProjectedEventCounts.SingleAsync(c => c.EventType == OtherExchangeRoutingKey, cancellationToken);
        count.Count.Should().Be(1);
    }

    private async Task PublishAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var payload = $$"""{"Id":"{{messageId}}","OccurredAt":"2026-07-19T00:00:00+00:00"}""";

        await using var connection = new RabbitMqConnection(MsOptions.Create(_rabbitMqOptions));
        var eventBus = new RabbitMqEventBus(connection, MsOptions.Create(_rabbitMqOptions));
        await eventBus.PublishAsync(new EventEnvelope(RoutingKey, 1, payload), cancellationToken);
    }

    private const string OtherExchangeRoutingKey = "other-exchange.test-event";

    private async Task PublishToOtherExchangeAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var payload = $$"""{"Id":"{{messageId}}","OccurredAt":"2026-07-19T00:00:00+00:00"}""";

        // Same broker connection details as _rabbitMqOptions, but a
        // publish-side ExchangeName pointed at the second exchange - the
        // consumer under test is bound to that exchange via its own explicit
        // exchangeName argument, independent of this options object.
        var otherExchangeOptions = new RabbitMqOptions
        {
            Host = _rabbitMqOptions.Host,
            Port = _rabbitMqOptions.Port,
            Username = _rabbitMqOptions.Username,
            Password = _rabbitMqOptions.Password,
            VirtualHost = _rabbitMqOptions.VirtualHost,
            ExchangeName = OtherExchangeName,
        };

        await using var connection = new RabbitMqConnection(MsOptions.Create(otherExchangeOptions));
        var eventBus = new RabbitMqEventBus(connection, MsOptions.Create(otherExchangeOptions));
        await eventBus.PublishAsync(new EventEnvelope(OtherExchangeRoutingKey, 1, payload), cancellationToken);
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

    private DeduplicatingEventConsumer CreateConsumer(ServiceProvider provider, out string queueName)
    {
        queueName = $"gbp.economy.log-projector.test.{Guid.NewGuid():N}";

        return new DeduplicatingEventConsumer(
            new RabbitMqConnection(MsOptions.Create(_rabbitMqOptions)),
            MsOptions.Create(_rabbitMqOptions),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IInboxFaultInjector>(),
            TimeProvider.System,
            NullLogger<DeduplicatingEventConsumer>.Instance,
            queueName);
    }

    private OtherExchangeConsumer CreateOtherExchangeConsumer(ServiceProvider provider, out string queueName)
    {
        queueName = $"gbp.test-other.consumer.test.{Guid.NewGuid():N}";

        return new OtherExchangeConsumer(
            new RabbitMqConnection(MsOptions.Create(_rabbitMqOptions)),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IInboxFaultInjector>(),
            TimeProvider.System,
            NullLogger<OtherExchangeConsumer>.Instance,
            OtherExchangeName,
            queueName);
    }

    private ServiceProvider BuildProvider(IInboxFaultInjector faultInjector)
    {
        var services = new ServiceCollection();
        services.AddDbContext<EconomyDbContext>(o => o.UseNpgsql(_connectionString));
        services.AddSingleton(faultInjector);
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

    // Minimal InboxConsumerBase subclass, test-only: exists only to prove
    // exchangeName controls the bind, independent of options.Value.ExchangeName
    // (which stays pointed at gbp.economy throughout). Side effect mirrors
    // DeduplicatingEventConsumer.ApplySideEffectAsync - same upsert, nothing
    // new to verify there.
    private sealed class OtherExchangeConsumer(
        IRabbitMqConnection connection,
        IServiceScopeFactory scopeFactory,
        IInboxFaultInjector faultInjector,
        TimeProvider timeProvider,
        ILogger<OtherExchangeConsumer> logger,
        string exchangeName,
        string queueName)
        : InboxConsumerBase<EconomyDbContext>(
            connection, scopeFactory, faultInjector, timeProvider, logger, exchangeName, queueName, RoutingKeys)
    {
        private static readonly string[] RoutingKeys = [OtherExchangeRoutingKey];

        protected override async Task ApplySideEffectAsync(
            EconomyDbContext dbContext, Guid messageId, string routingKey, ReadOnlyMemory<byte> body, CancellationToken cancellationToken) =>
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO projected_event_counts (event_type, count)
                VALUES ({routingKey}, 1)
                ON CONFLICT (event_type) DO UPDATE SET count = projected_event_counts.count + 1
                """,
                cancellationToken);
    }
}
