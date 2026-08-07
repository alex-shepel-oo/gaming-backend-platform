using System.Globalization;
using BuildingBlocks.Auth;
using BuildingBlocks.Testing;
using EconomyService.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EconomyService.Tests.Integration;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace EconomyService.Tests.Integration.Fixtures;

public sealed class EconomyApiFactory : WebApplicationFactory<Program>
{
    public FakeJwksHandler JwksHandler { get; } = new(TestJwks.JwksJson);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(TestContainerImages.Postgres)
        .WithDatabase("economy_db")
        .WithUsername("economy")
        .WithPassword("economy_test_password")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    private Respawner _respawner = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var optionsBuilder = new DbContextOptionsBuilder<EconomyDbContext>().UseNpgsql(_container.GetConnectionString());
        await using (var dbContext = new EconomyDbContext(optionsBuilder.Options))
        {
            await dbContext.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
        });
    }

    public async Task ResetAsync()
    {
        // OutboxDispatcherService keeps polling in the background for the
        // whole life of this factory. Its poll transaction and this reset
        // can occasionally lock the same tables in opposite order - Postgres
        // reports that as a deadlock and aborts one side, which Postgres
        // itself flags transient. A couple of retries is the correct
        // response to a transient error, not a workaround for a real bug.
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var connection = new NpgsqlConnection(_container.GetConnectionString());
            await connection.OpenAsync();

            try
            {
                await _respawner.ResetAsync(connection);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DeadlockDetected && attempt < maxAttempts)
            {
            }
        }
    }

    public async Task StopContainerAsync() => await _container.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:EconomyDb"] = _container.GetConnectionString(),
                ["Jwt:JwksUri"] = "https://identity.test/.well-known/jwks.json",

                // Seeding/OpenAPI now default to enabled independent of ASPNETCORE_ENVIRONMENT
                // (see SeedingOptions/ApiOptions), whereas "Testing" previously never tripped
                // IsDevelopment() and so never seeded automatically. Pin both off here so this
                // shared factory keeps that exact behavior: every existing test that wants
                // seeded data already asks DevelopmentSeeder for it explicitly.
                ["Seeding:Enabled"] = "false",
                ["Api:ExposeOpenApi"] = "false",
                ["RabbitMq:Host"] = RabbitMqTestBroker.Container.Hostname,
                ["RabbitMq:Port"] = RabbitMqTestBroker.Container.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
                ["RabbitMq:Username"] = "guest",
                ["RabbitMq:Password"] = "guest",
            }));

        // Real JwksKeyCache, real background refresher, just the HTTP call at the bottom
        // replaced, so these tests exercise the same key-resolution path production does.
        builder.ConfigureServices(services => services
            .AddHttpClient<IJwksKeyCache, JwksKeyCache>()
            .ConfigurePrimaryHttpMessageHandler(() => JwksHandler));
    }
}
