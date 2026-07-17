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

    public async Task ResetAsync()
    {
        await postgres.ResetAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDb"] = postgres.ConnectionString,
                ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes-long",
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(TimeProvider);
        });
    }
}

[CollectionDefinition(nameof(IdentityApiCollectionDefinition))]
public sealed class IdentityApiCollectionDefinition : ICollectionFixture<PostgresFixture>;
