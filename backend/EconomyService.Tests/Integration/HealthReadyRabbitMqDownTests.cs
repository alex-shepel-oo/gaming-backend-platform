using System.Globalization;
using System.Net;
using AwesomeAssertions;
using BuildingBlocks.Messaging;
using EconomyService.Auth;
using EconomyService.Persistence;
using EconomyService.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace EconomyService.Tests.Integration;

// Its own Postgres and RabbitMQ containers, deliberately not the shared
// RabbitMqTestBroker/EconomyApiFactory that every other test class in this
// namespace relies on: this class kills its broker mid-test to prove the
// readiness probe, and a shared broker taken down here would fail every
// other test class racing against it.
[TestFixture]
public sealed class HealthReadyRabbitMqDownTests : IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("economy_db")
        .WithUsername("economy")
        .WithPassword("economy_test_password")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

        var optionsBuilder = new DbContextOptionsBuilder<EconomyDbContext>().UseNpgsql(_postgres.GetConnectionString());
        await using (var dbContext = new EconomyDbContext(optionsBuilder.Options))
        {
            await dbContext.Database.MigrateAsync();
        }

        // UserEmailConfirmedConsumer binds its own queue to gbp.identity at
        // startup, independent of this service's own gbp.economy topology -
        // this isolated container has no identity-service standing in to
        // declare it, so the bind fails and the whole host goes down unless
        // it's declared here first (mirrors RabbitMqTestBroker's fix for the
        // shared container).
        var identityOptions = new RabbitMqOptions
        {
            Host = _rabbitMq.Hostname,
            Port = _rabbitMq.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
        };

        await using (var topologyConnection = new RabbitMqConnection(MsOptions.Create(identityOptions)))
        await using (var channel = await topologyConnection.CreateChannelAsync(TestContext.CurrentContext.CancellationToken))
        {
            await channel.ExchangeDeclareAsync(
                "gbp.identity",
                ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: TestContext.CurrentContext.CancellationToken);
        }

        // The broker must be up when the factory builds the host: topology
        // declaration runs as a hosted service at startup and fails fast if
        // it can't reach RabbitMQ (A.1), so a client can't even be created
        // against an already-dead broker.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:EconomyDb"] = _postgres.GetConnectionString(),
                    ["Jwt:JwksUri"] = "https://identity.test/.well-known/jwks.json",
                    ["RabbitMq:Host"] = _rabbitMq.Hostname,
                    ["RabbitMq:Port"] = _rabbitMq.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
                    ["RabbitMq:Username"] = "guest",
                    ["RabbitMq:Password"] = "guest",

                    // Not what this class exercises -- keeps this isolated host's DB state
                    // (and startup path) identical to every other test's "Testing" default.
                    ["Seeding:Enabled"] = "false",
                    ["Api:ExposeOpenApi"] = "false",
                }));

            // The app's startup path does one blocking JWKS refresh before it accepts any
            // requests, unrelated to what this test itself exercises -- it still needs
            // somewhere to succeed against.
            builder.ConfigureServices(services => services
                .AddHttpClient<IJwksKeyCache, JwksKeyCache>()
                .ConfigurePrimaryHttpMessageHandler(() => new FakeJwksHandler(TestJwks.JwksJson)));
        });

        // Force the client's connection to open (and topology to declare)
        // while the broker is still alive, before it gets stopped below.
        using var warmupClient = _factory.CreateClient();
        await warmupClient.GetAsync(new Uri("/health/ready", UriKind.Relative));

        // The health check above only proves the shared RabbitMQ *connection*
        // is open - it says nothing about whether this host's own background
        // consumers (DeduplicatingEventConsumer, UserEmailConfirmedConsumer)
        // have finished their own independent declare/bind/consume startup
        // sequence on their own channels. Each is started as a fire-and-
        // forget background task by BackgroundService.StartAsync, so "warmup
        // GET returned 200" can race ahead of either one still being mid-
        // BasicConsumeAsync when the test stops the broker below. If that
        // happens, the broker's own graceful shutdown sends a
        // CONNECTION_FORCED close to the still-connecting consumer,
        // OperationInterruptedException escapes ExecuteAsync unhandled, and
        // the host's default BackgroundServiceExceptionBehavior.StopHost
        // tears the whole TestServer down before the test method ever runs.
        // Wait for both consumers to actually be attached first.
        await WaitForConsumerReadyAsync("gbp.economy.log-projector", TestContext.CurrentContext.CancellationToken);
        await WaitForConsumerReadyAsync("gbp.economy.welcome-grant", TestContext.CurrentContext.CancellationToken);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        _factory.Dispose();
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }

    // Polls until the queue has a consumer actually attached (not just
    // declared) - ConsumerCount only reaches 1 once BasicConsumeAsync has
    // completed, which is the specific point the startup race above needs
    // to have passed. Same poll-until-ready shape as
    // WelcomeGrantConsumerTests/ConsumerDeduplicationTests use elsewhere in
    // this namespace, just checking consumer attachment rather than mere
    // queue existence.
    private async Task WaitForConsumerReadyAsync(string queueName, CancellationToken cancellationToken)
    {
        var options = new RabbitMqOptions
        {
            Host = _rabbitMq.Hostname,
            Port = _rabbitMq.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        while (!cts.IsCancellationRequested)
        {
            try
            {
                await using var connection = new RabbitMqConnection(MsOptions.Create(options));
                await using var channel = await connection.CreateChannelAsync(cts.Token);
                var declareOk = await channel.QueueDeclarePassiveAsync(queueName, cts.Token);

                if (declareOk.ConsumerCount >= 1)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);
        }

        Assert.Fail($"Consumer for queue '{queueName}' was not attached within the timeout.");
    }

    [Test]
    public async Task HealthReady_RabbitMqUnreachable_Returns503()
    {
        await _rabbitMq.StopAsync();

        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
