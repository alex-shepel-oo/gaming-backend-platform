using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Auth;

namespace NotificationService.Tests.Integration.Fixtures;

public sealed class NotificationApiFactory(RabbitMqFixture rabbitMq) : WebApplicationFactory<Program>
{
    public FakeJwksHandler JwksHandler { get; } = new(TestJwks.JwksJson);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:JwksUri"] = "https://identity.test/.well-known/jwks.json",
                ["RabbitMq:Host"] = rabbitMq.Container.Hostname,
                ["RabbitMq:Port"] = rabbitMq.Container.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
                ["RabbitMq:Username"] = "guest",
                ["RabbitMq:Password"] = "guest",
            }));

        // Real JwksKeyCache, real background refresher, just the HTTP call at the bottom
        // replaced -- so these tests exercise the same key-resolution path production does.
        builder.ConfigureServices(services => services
            .AddHttpClient<IJwksKeyCache, JwksKeyCache>()
            .ConfigurePrimaryHttpMessageHandler(() => JwksHandler));
    }
}
