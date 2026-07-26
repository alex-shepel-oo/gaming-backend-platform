using System.Globalization;
using System.Net;
using AwesomeAssertions;
using BuildingBlocks.Messaging;
using EconomyService.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
                    ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes-long",
                    ["RabbitMq:Host"] = _rabbitMq.Hostname,
                    ["RabbitMq:Port"] = _rabbitMq.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
                    ["RabbitMq:Username"] = "guest",
                    ["RabbitMq:Password"] = "guest",
                }));
        });

        // Force the client's connection to open (and topology to declare)
        // while the broker is still alive, before it gets stopped below.
        using var warmupClient = _factory.CreateClient();
        await warmupClient.GetAsync(new Uri("/health/ready", UriKind.Relative));
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        _factory.Dispose();
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
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
