using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using EconomyService.Domain;
using EconomyService.Messaging;
using EconomyService.Options;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using NUnit.Framework;
using RabbitMQ.Client;
using Respawn;
using Testcontainers.PostgreSql;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace EconomyService.Tests.Integration;

[TestFixture]
public sealed class OutboxDispatcherServiceTests : IAsyncDisposable
{
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
    public async Task Dispatch_UnsentMessage_PublishesToBoundQueueAndMarksProcessed()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var routingKey = $"test.dispatch.{Guid.NewGuid():N}";
        const string payload = """{"hello":"world"}""";
        var messageId = await SeedUnsentMessageAsync(routingKey, payload);

        await using var rabbitConnection = new RabbitMqConnection(MsOptions.Create(_rabbitMqOptions));
        await using var consumerChannel = await rabbitConnection.CreateChannelAsync(cancellationToken);
        var queueName = (await consumerChannel.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: cancellationToken)).QueueName;
        await consumerChannel.QueueBindAsync(queueName, _rabbitMqOptions.ExchangeName, routingKey, cancellationToken: cancellationToken);

        var eventBus = new RabbitMqEventBus(rabbitConnection, MsOptions.Create(_rabbitMqOptions));
        await using var provider = BuildProvider();
        using var dispatcher = CreateDispatcher(provider, eventBus, maxAttempts: 5);

        await dispatcher.StartAsync(cancellationToken);
        try
        {
            var delivery = await WaitForDeliveryAsync(consumerChannel, queueName, TimeSpan.FromSeconds(15));

            delivery.Should().NotBeNull();

            // jsonb round-trips through Postgres reformatted (e.g. a space
            // after ':'), so compare parsed JSON rather than raw text.
            var deliveredJson = Encoding.UTF8.GetString(delivery!.Body.ToArray());
            JsonNode.Parse(deliveredJson)!.ToJsonString().Should().Be(JsonNode.Parse(payload)!.ToJsonString());

            await WaitUntilAsync(
                async () =>
                {
                    await using var verify = CreateDbContext();
                    var message = await verify.OutboxMessages.SingleAsync(m => m.Id == messageId, cancellationToken);
                    return message.ProcessedAt is not null;
                },
                TimeSpan.FromSeconds(15));
        }
        finally
        {
            await dispatcher.StopAsync(cancellationToken);
        }
    }

    [Test]
    public async Task Dispatch_TransientPublishFailures_RetriesAndEventuallyMarksProcessedWithAttemptsRecorded()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var messageId = await SeedUnsentMessageAsync("test.transient", """{"attempt":"eventually-ok"}""");

        var failuresRemaining = 2;
        var eventBus = Substitute.For<IEventBus>();
        eventBus.PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Decrement(ref failuresRemaining) >= 0)
                {
                    return Task.FromException(new InvalidOperationException("transient publish failure"));
                }

                return Task.CompletedTask;
            });

        await using var provider = BuildProvider();
        using var dispatcher = CreateDispatcher(provider, eventBus, maxAttempts: 5);

        await dispatcher.StartAsync(cancellationToken);
        try
        {
            await WaitUntilAsync(
                async () =>
                {
                    await using var verify = CreateDbContext();
                    var message = await verify.OutboxMessages.SingleAsync(m => m.Id == messageId, cancellationToken);
                    return message.ProcessedAt is not null;
                },
                TimeSpan.FromSeconds(15));
        }
        finally
        {
            await dispatcher.StopAsync(cancellationToken);
        }

        await using var final = CreateDbContext();
        var finalMessage = await final.OutboxMessages.SingleAsync(m => m.Id == messageId, cancellationToken);
        finalMessage.ProcessedAt.Should().NotBeNull();
        finalMessage.Attempts.Should().Be(2);
    }

    [Test]
    public async Task Dispatch_PublishAlwaysFails_ParksAfterMaxAttemptsAndStopsRetrying()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        const int maxAttempts = 3;
        var messageId = await SeedUnsentMessageAsync("test.always-fails", """{"attempt":"never-ok"}""");

        var eventBus = Substitute.For<IEventBus>();
        eventBus.PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("permanent publish failure")));

        await using var provider = BuildProvider();
        using var dispatcher = CreateDispatcher(provider, eventBus, maxAttempts, pollIntervalSeconds: 1);

        await dispatcher.StartAsync(cancellationToken);
        try
        {
            await WaitUntilAsync(
                async () =>
                {
                    await using var verify = CreateDbContext();
                    var message = await verify.OutboxMessages.SingleAsync(m => m.Id == messageId, cancellationToken);
                    return message.Attempts >= maxAttempts;
                },
                TimeSpan.FromSeconds(15));

            // Give the dispatcher a couple more poll cycles: a message parked
            // at the attempts ceiling must stay untouched, not keep climbing.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
        finally
        {
            await dispatcher.StopAsync(cancellationToken);
        }

        await using var final = CreateDbContext();
        var finalMessage = await final.OutboxMessages.SingleAsync(m => m.Id == messageId, cancellationToken);
        finalMessage.ProcessedAt.Should().BeNull();
        finalMessage.Attempts.Should().Be(maxAttempts);
    }

    [Test]
    public async Task Dispatch_TwoConcurrentDispatchers_NeverPublishTheSameRowTwice()
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            await ResetDatabaseAsync();
            await RunConcurrentDispatchRaceOnceAsync();
        }
    }

    private async Task RunConcurrentDispatchRaceOnceAsync()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        const int messageCount = 40;

        var messageIds = new List<Guid>(messageCount);
        for (var i = 0; i < messageCount; i++)
        {
            messageIds.Add(await SeedUnsentMessageAsync("test.race", $$"""{"index":{{i}}}"""));
        }

        var publishCounts = new ConcurrentDictionary<string, int>();
        var sharedEventBus = Substitute.For<IEventBus>();
        sharedEventBus.PublishAsync(Arg.Any<EventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var envelope = callInfo.Arg<EventEnvelope>()!;
                publishCounts.AddOrUpdate(envelope.Payload, 1, (_, count) => count + 1);
                return Task.CompletedTask;
            });

        await using var providerA = BuildProvider();
        await using var providerB = BuildProvider();
        using var dispatcherA = CreateDispatcher(providerA, sharedEventBus, maxAttempts: 5, batchSize: messageCount);
        using var dispatcherB = CreateDispatcher(providerB, sharedEventBus, maxAttempts: 5, batchSize: messageCount);

        await Task.WhenAll(dispatcherA.StartAsync(cancellationToken), dispatcherB.StartAsync(cancellationToken));
        try
        {
            await WaitUntilAsync(
                async () =>
                {
                    await using var verify = CreateDbContext();
                    var unprocessedCount = await verify.OutboxMessages
                        .CountAsync(m => m.ProcessedAt == null, cancellationToken);
                    return unprocessedCount == 0;
                },
                TimeSpan.FromSeconds(20));
        }
        finally
        {
            await Task.WhenAll(dispatcherA.StopAsync(cancellationToken), dispatcherB.StopAsync(cancellationToken));
        }

        publishCounts.Should().HaveCount(messageCount);
        publishCounts.Values.Should().OnlyContain(count => count == 1);

        await using var final = CreateDbContext();
        var stillUnprocessed = await final.OutboxMessages.CountAsync(m => m.ProcessedAt == null, cancellationToken);
        stillUnprocessed.Should().Be(0);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    private static async Task<BasicGetResult?> WaitForDeliveryAsync(IChannel channel, string queueName, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            var result = await channel.BasicGetAsync(queueName, autoAck: true, cts.Token);
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);
        }

        return null;
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

    private async Task<Guid> SeedUnsentMessageAsync(string type, string payload)
    {
        await using var dbContext = CreateDbContext();
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Version = 1,
            Payload = payload,
            OccurredAt = DateTimeOffset.UtcNow,
            ProcessedAt = null,
            Attempts = 0,
        };
        dbContext.OutboxMessages.Add(message);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        return message.Id;
    }

    private static OutboxDispatcherService CreateDispatcher(
        ServiceProvider provider, IEventBus eventBus, int maxAttempts, int pollIntervalSeconds = 1, int batchSize = 20) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            eventBus,
            MsOptions.Create(new OutboxDispatcherOptions
            {
                PollIntervalSeconds = pollIntervalSeconds,
                BatchSize = batchSize,
                MaxAttempts = maxAttempts,
            }),
            TimeProvider.System,
            NullLogger<OutboxDispatcherService>.Instance);

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<EconomyDbContext>(o => o.UseNpgsql(_connectionString));
        return services.BuildServiceProvider();
    }

    private EconomyDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EconomyDbContext>().UseNpgsql(_connectionString);
        return new EconomyDbContext(optionsBuilder.Options);
    }
}
