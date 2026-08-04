using System.Globalization;
using System.Net;
using AwesomeAssertions;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Auth;
using NotificationService.Tests.Integration.Fixtures;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace NotificationService.Tests.Integration;

// Its own RabbitMQ container, deliberately not the shared RabbitMqFixture every
// other test class in this namespace relies on: this class kills its broker
// mid-test to prove the readiness probe, and a shared broker taken down here
// would fail every other test class racing against it.
public sealed class HealthReadyRabbitMqDownTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    public async ValueTask InitializeAsync()
    {
        await _rabbitMq.StartAsync();

        // BalanceChangedConsumer binds its queue to this exchange on host
        // startup; nothing else in this test declares it first, so the bind
        // would fail (and take the host down with it) without this.
        var options = MsOptions.Create(new RabbitMqOptions
        {
            Host = _rabbitMq.Hostname,
            Port = _rabbitMq.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
        });

        await using (var connection = new RabbitMqConnection(options))
        await using (var channel = await connection.CreateChannelAsync())
        {
            await channel.ExchangeDeclareAsync(options.Value.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        }

        // The broker must be up when the factory builds the host: IRabbitMqConnection
        // is resolved lazily on first use, but the readiness check triggers that on
        // the very first request, so a client can't warm up against an already-dead
        // broker.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:JwksUri"] = "https://identity.test/.well-known/jwks.json",
                    ["RabbitMq:Host"] = _rabbitMq.Hostname,
                    ["RabbitMq:Port"] = _rabbitMq.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
                    ["RabbitMq:Username"] = "guest",
                    ["RabbitMq:Password"] = "guest",
                }));

            // The app's startup path does one blocking JWKS refresh before it accepts any
            // requests, unrelated to what this test itself exercises -- it still needs
            // somewhere to succeed against.
            builder.ConfigureServices(services => services
                .AddHttpClient<IJwksKeyCache, JwksKeyCache>()
                .ConfigurePrimaryHttpMessageHandler(() => new FakeJwksHandler(TestJwks.JwksJson)));
        });

        using var warmupClient = _factory.CreateClient();
        await warmupClient.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _factory.Dispose();
        await _rabbitMq.DisposeAsync();
    }

    [Fact]
    public async Task HealthReady_RabbitMqUnreachable_Returns503()
    {
        await _rabbitMq.StopAsync(TestContext.Current.CancellationToken);

        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
