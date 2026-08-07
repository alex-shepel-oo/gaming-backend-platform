using System.Globalization;
using System.Net;
using AwesomeAssertions;
using BuildingBlocks.Auth;
using EconomyService.Persistence;
using EconomyService.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace EconomyService.Tests.Integration;

// Seeding__Enabled/Api__ExposeOpenApi replaced the old IsDevelopment() gate around
// DevelopmentSeeder/MapOpenApi; these tests boot the app under environments and flag
// combinations EconomyApiFactory's own "Testing" default never exercises, to prove both
// flags actually drive the gate rather than the environment name.
[TestFixture]
public sealed class StartupOptionsTests : IAsyncDisposable
{
    private EconomyApiFactory _factory = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _factory = new EconomyApiFactory();
        await _factory.InitializeAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        await _factory.StopContainerAsync();
        await _factory.DisposeAsync();
    }

    [SetUp]
    public async Task SetUpAsync() => await _factory.ResetAsync();

    [Test]
    public async Task SeedingEnabled_InProduction_Seeds()
    {
        using var scopedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Seeding:Enabled"] = "true" }));
        });

        using var client = scopedFactory.CreateClient();

        await using var scope = scopedFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();
        (await dbContext.Currencies.AnyAsync(TestContext.CurrentContext.CancellationToken)).Should().BeTrue();
    }

    [Test]
    public async Task SeedingDisabled_InDevelopment_DoesNotSeed()
    {
        using var scopedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Seeding:Enabled"] = "false" }));
        });

        using var client = scopedFactory.CreateClient();

        await using var scope = scopedFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();
        (await dbContext.Currencies.AnyAsync(TestContext.CurrentContext.CancellationToken)).Should().BeFalse();
    }

    [Test]
    public async Task ExposeOpenApiDisabled_InDevelopment_HidesOpenApi()
    {
        using var scopedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Api:ExposeOpenApi"] = "false" }));
        });

        using var client = scopedFactory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json", TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ExposeOpenApiEnabled_InProduction_ExposesOpenApi()
    {
        using var scopedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Api:ExposeOpenApi"] = "true" }));
        });

        using var client = scopedFactory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json", TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Deliberately bypasses EconomyApiFactory: that shared factory pins Seeding:Enabled and
    // Api:ExposeOpenApi off (see its own comment) so every other test in this assembly keeps
    // "Testing" environment's pre-existing no-auto-seed behavior. Proving the *unset* default
    // means building a host with neither key present at all, which the shared factory can no
    // longer produce once it has bound them explicitly.
    [Test]
    public async Task DefaultFlags_InDevelopment_MatchesTodaysExperience()
    {
        using var defaultFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:EconomyDb"] = _factory.ConnectionString,
                    ["Jwt:JwksUri"] = "https://identity.test/.well-known/jwks.json",
                    ["RabbitMq:Host"] = RabbitMqTestBroker.Container.Hostname,
                    ["RabbitMq:Port"] = RabbitMqTestBroker.Container.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
                    ["RabbitMq:Username"] = "guest",
                    ["RabbitMq:Password"] = "guest",
                }));

            builder.ConfigureServices(services => services
                .AddHttpClient<IJwksKeyCache, JwksKeyCache>()
                .ConfigurePrimaryHttpMessageHandler(() => new FakeJwksHandler(TestJwks.JwksJson)));
        });

        using var client = defaultFactory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json", TestContext.CurrentContext.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = defaultFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();
        (await dbContext.Currencies.AnyAsync(TestContext.CurrentContext.CancellationToken)).Should().BeTrue();
    }
}
