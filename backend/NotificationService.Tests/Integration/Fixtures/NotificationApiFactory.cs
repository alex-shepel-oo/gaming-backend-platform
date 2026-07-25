using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NotificationService.Tests.Integration.Fixtures;

public sealed class NotificationApiFactory(RabbitMqFixture rabbitMq) : WebApplicationFactory<Program>
{
    public const string SigningKey = "integration-test-signing-key-at-least-32-bytes-long";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:Key"] = SigningKey,
                ["RabbitMq:Host"] = rabbitMq.Container.Hostname,
                ["RabbitMq:Port"] = rabbitMq.Container.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
                ["RabbitMq:Username"] = "guest",
                ["RabbitMq:Password"] = "guest",
            }));
    }
}
