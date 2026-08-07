using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using AwesomeAssertions;
using IdentityService.Persistence;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

// Seeding__Enabled/Api__ExposeOpenApi replaced the old IsDevelopment() gate around
// DevelopmentSeeder/MapOpenApi; these tests boot the app under environments and flag
// combinations IdentityApiFactory's own "Testing" default never exercises, to prove both
// flags actually drive the gate rather than the environment name.
[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class StartupOptionsTests(IdentityApiFactory factory, PostgresFixture postgres, RabbitMqFixture rabbitMq)
    : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SeedingEnabled_InProduction_Seeds()
    {
        using var scopedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Seeding:Enabled"] = "true" }));
        });

        using var client = scopedFactory.CreateClient();

        await using var scope = scopedFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await dbContext.Games.AnyAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task SeedingDisabled_InDevelopment_DoesNotSeed()
    {
        using var scopedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Seeding:Enabled"] = "false" }));
        });

        using var client = scopedFactory.CreateClient();

        await using var scope = scopedFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await dbContext.Games.AnyAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task ExposeOpenApiDisabled_InDevelopment_HidesOpenApi()
    {
        using var scopedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Api:ExposeOpenApi"] = "false" }));
        });

        using var client = scopedFactory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExposeOpenApiEnabled_InProduction_ExposesOpenApi()
    {
        using var scopedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Api:ExposeOpenApi"] = "true" }));
        });

        using var client = scopedFactory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Deliberately bypasses IdentityApiFactory: that shared factory pins Seeding:Enabled and
    // Api:ExposeOpenApi off (see its own comment) so every other test in this assembly keeps
    // "Testing" environment's pre-existing no-auto-seed behavior. Proving the *unset* default
    // means building a host with neither key present at all, which the shared factory can no
    // longer produce once it has bound them explicitly.
    [Fact]
    public async Task DefaultFlags_InDevelopment_MatchesTodaysExperience()
    {
        var testPrivateKeyPem = RSA.Create(2048).ExportPkcs8PrivateKeyPem();

        using var defaultFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:IdentityDb"] = postgres.ConnectionString,
                    ["Jwt:PrivateKeyPem"] = testPrivateKeyPem,
                    ["RabbitMq:Host"] = rabbitMq.Hostname,
                    ["RabbitMq:Port"] = rabbitMq.Port.ToString(CultureInfo.InvariantCulture),
                    ["RabbitMq:Username"] = "guest",
                    ["RabbitMq:Password"] = "guest",
                    ["RabbitMq:ExchangeName"] = "gbp.identity",
                }));
        });

        using var client = defaultFactory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = defaultFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await dbContext.Games.AnyAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
    }
}
