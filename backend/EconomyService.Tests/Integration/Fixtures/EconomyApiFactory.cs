using EconomyService.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace EconomyService.Tests.Integration.Fixtures;

public sealed class EconomyApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "integration-test-signing-key-at-least-32-bytes-long";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("economy_db")
        .WithUsername("economy")
        .WithPassword("economy_test_password")
        .Build();

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
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async Task StopContainerAsync() => await _container.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) => configBuilder.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:EconomyDb"] = _container.GetConnectionString(),
                ["Jwt:Key"] = SigningKey,
            }));
    }
}
