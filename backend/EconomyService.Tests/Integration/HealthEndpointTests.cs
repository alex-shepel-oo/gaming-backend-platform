using System.Globalization;
using System.Net;
using AwesomeAssertions;
using EconomyService.Auth;
using EconomyService.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace EconomyService.Tests.Integration;

[TestFixture]
public sealed class HealthEndpointTests : IDisposable
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() =>
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:JwksUri"] = "https://identity.test/.well-known/jwks.json",
                    // Deliberately unreachable: closed port, short timeout, so the
                    // readiness check fails fast instead of hanging on Npgsql's
                    // default connect timeout.
                    ["ConnectionStrings:EconomyDb"] =
                        "Host=127.0.0.1;Port=1;Database=nonexistent;Username=nobody;Password=nobody;Timeout=2",
                    ["RabbitMq:Host"] = RabbitMqTestBroker.Container.Hostname,
                    ["RabbitMq:Port"] = RabbitMqTestBroker.Container.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
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

    [OneTimeTearDown]
    public void OneTimeTearDown() => Dispose();

    public void Dispose() => _factory.Dispose();

    [Test]
    public async Task Health_Returns200()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task HealthReady_DatabaseUnreachable_Returns503()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
