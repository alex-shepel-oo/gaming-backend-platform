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

public sealed class IdentityApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
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
                ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes-long",

                // High enough that the many unrelated tests sharing this one host and one
                // rate limiter instance never trip a partition by accident. Tests that
                // actually exercise the limiter override a single partition's limit down
                // via WithWebHostBuilder instead of relying on these defaults.
                ["RateLimiting:LoginPermitLimit"] = "100000",
                ["RateLimiting:RegisterPermitLimit"] = "100000",
                ["RateLimiting:ConfirmEmailPermitLimit"] = "100000",
                ["RateLimiting:ResendVerificationPermitLimit"] = "100000",
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
public sealed class IdentityApiCollectionDefinition : ICollectionFixture<PostgresFixture>;
