using System.Globalization;
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
public sealed class CorrelationIdMiddlewareTests : IDisposable
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
                    // Irrelevant to this test (only /health is hit, which
                    // touches neither), but the outbox dispatcher polls in
                    // the background regardless of which endpoint a request
                    // hits, so it needs a connection string to fail against
                    // rather than an unset one.
                    ["ConnectionStrings:EconomyDb"] =
                        "Host=127.0.0.1;Port=1;Database=nonexistent;Username=nobody;Password=nobody;Timeout=2",
                    ["RabbitMq:Host"] = RabbitMqTestBroker.Container.Hostname,
                    ["RabbitMq:Port"] = RabbitMqTestBroker.Container.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
                    ["RabbitMq:Username"] = "guest",
                    ["RabbitMq:Password"] = "guest",

                    // Seeding would hit the deliberately-unreachable database above and take
                    // the whole host down before either test below ever runs.
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

    [OneTimeTearDown]
    public void OneTimeTearDown() => Dispose();

    public void Dispose() => _factory.Dispose();

    [Test]
    public async Task Health_NoCorrelationIdHeader_RespondsWithGeneratedId()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

        response.Headers.TryGetValues("X-Correlation-Id", out var values).Should().BeTrue();
        values!.Single().Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Health_CorrelationIdHeaderProvided_EchoesSameId()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-Id", "client-supplied-id");

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.Headers.GetValues("X-Correlation-Id").Single().Should().Be("client-supplied-id");
    }
}
