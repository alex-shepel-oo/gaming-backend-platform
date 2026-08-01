using System.Globalization;
using System.Security.Cryptography;
using IdentityService.Services.Email;
using IdentityService.Tests.Integration.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace IdentityService.Tests.Integration.Fixtures;

public sealed class IdentityApiFactory(PostgresFixture postgres, RabbitMqFixture rabbitMq) : WebApplicationFactory<Program>
{
    // RSA-2048 test-only key pair, generated fresh each test run rather than hardcoded --
    // avoids ever committing anything that looks like real key material (gitleaks and
    // friends flag PEM blocks on sight, regardless of context), and matches the same
    // runtime-generation convention EconomyService.Tests/NotificationService.Tests/
    // ApiGateway.Tests already use for their own JWKS test fixtures.
    private static readonly string TestPrivateKeyPem = RSA.Create(2048).ExportPkcs8PrivateKeyPem();

    public FakeTimeProvider TimeProvider { get; } = new(DateTimeOffset.UtcNow);

    public RecordingEmailSender EmailSender { get; } = new();

    public async Task ResetAsync()
    {
        await postgres.ResetAsync();
        EmailSender.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDb"] = postgres.ConnectionString,
                ["Jwt:PrivateKeyPem"] = TestPrivateKeyPem,

                // Seeding/OpenAPI now default to enabled independent of ASPNETCORE_ENVIRONMENT
                // (see SeedingOptions/ApiOptions), whereas "Testing" previously never tripped
                // IsDevelopment() and so never seeded automatically. Pin both off here so this
                // shared factory keeps that exact behavior -- every existing test that wants
                // seeded data already asks DevelopmentSeeder for it explicitly.
                ["Seeding:Enabled"] = "false",
                ["Api:ExposeOpenApi"] = "false",

                ["RabbitMq:Host"] = rabbitMq.Hostname,
                ["RabbitMq:Port"] = rabbitMq.Port.ToString(CultureInfo.InvariantCulture),
                ["RabbitMq:Username"] = "guest",
                ["RabbitMq:Password"] = "guest",
                ["RabbitMq:ExchangeName"] = "gbp.identity",

                // High enough that the many unrelated tests sharing this one host and one
                // rate limiter instance never trip a partition by accident. Tests that
                // actually exercise the limiter override a single partition's limit down
                // via WithWebHostBuilder instead of relying on these defaults.
                ["RateLimiting:LoginPermitLimit"] = "100000",
                ["RateLimiting:RegisterPermitLimit"] = "100000",
                ["RateLimiting:ConfirmEmailPermitLimit"] = "100000",
                ["RateLimiting:ResendVerificationPermitLimit"] = "100000",
                ["RateLimiting:RequestPasswordResetPermitLimit"] = "100000",
                ["RateLimiting:ResetPasswordPermitLimit"] = "100000",
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(TimeProvider);

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }
}

[CollectionDefinition(nameof(IdentityApiCollectionDefinition))]
public sealed class IdentityApiCollectionDefinition : ICollectionFixture<PostgresFixture>, ICollectionFixture<RabbitMqFixture>;
