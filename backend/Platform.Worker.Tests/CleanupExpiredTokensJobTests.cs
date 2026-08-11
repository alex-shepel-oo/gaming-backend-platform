using AwesomeAssertions;
using BuildingBlocks.Messaging.Outbox;
using BuildingBlocks.Testing;
using EconomyService.Domain;
using EconomyService.Persistence;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using NSubstitute;
using NUnit.Framework;
using Platform.Worker.Jobs;
using Platform.Worker.Options;
using Platform.Worker.Persistence;
using Quartz;
using Respawn;
using Testcontainers.PostgreSql;

namespace Platform.Worker.Tests;

[TestFixture]
public sealed class CleanupExpiredTokensJobTests : IAsyncDisposable
{
    private readonly PostgreSqlContainer _identityContainer = new PostgreSqlBuilder(TestContainerImages.Postgres)
        .WithDatabase("identity_db")
        .WithUsername("identity")
        .WithPassword("identity_test_password")
        .Build();

    private readonly PostgreSqlContainer _economyContainer = new PostgreSqlBuilder(TestContainerImages.Postgres)
        .WithDatabase("economy_db")
        .WithUsername("economy")
        .WithPassword("economy_test_password")
        .Build();

    private string _identityConnectionString = null!;
    private string _economyConnectionString = null!;
    private Respawner _identityRespawner = null!;
    private Respawner _economyRespawner = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        await Task.WhenAll(_identityContainer.StartAsync(), _economyContainer.StartAsync());
        _identityConnectionString = _identityContainer.GetConnectionString();
        _economyConnectionString = _economyContainer.GetConnectionString();

        await using (var identityDbContext = CreateIdentityDbContext())
        {
            await identityDbContext.Database.MigrateAsync();
        }

        await using (var economyDbContext = CreateEconomyDbContext())
        {
            await economyDbContext.Database.MigrateAsync();
        }

        _identityRespawner = await CreateRespawnerAsync(_identityConnectionString);
        _economyRespawner = await CreateRespawnerAsync(_economyConnectionString);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await DisposeAsync();

    [SetUp]
    public async Task SetUpAsync()
    {
        await ResetAsync(_identityConnectionString, _identityRespawner);
        await ResetAsync(_economyConnectionString, _economyRespawner);
    }

    public async ValueTask DisposeAsync()
    {
        await _identityContainer.DisposeAsync();
        await _economyContainer.DisposeAsync();
    }

    [Test]
    public async Task Execute_DeletesExpiredAndRevokedFamiliesButKeepsLiveOnes()
    {
        var now = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
        var userId = await SeedUserAsync();

        await SeedRefreshTokenFamilyAsync(userId, now.AddDays(-1), revokedAt: null);
        await SeedRefreshTokenFamilyAsync(userId, now.AddDays(10), revokedAt: now.AddMinutes(-5));
        var liveFamilyId = await SeedRefreshTokenFamilyAsync(userId, now.AddDays(10), revokedAt: null);

        await RunJobAsync(now);

        await using var verifyContext = CreateIdentityDbContext();
        var remainingIds = await verifyContext.RefreshTokenFamilies.Select(f => f.Id).ToListAsync(TestContext.CurrentContext.CancellationToken);
        remainingIds.Should().ContainSingle().Which.Should().Be(liveFamilyId);
    }

    [Test]
    public async Task Execute_DeletesExpiredVerificationCodesButKeepsLiveOnes()
    {
        var now = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
        var expiredCodeUserId = await SeedUserAsync();
        var liveCodeUserId = await SeedUserAsync();

        // Two different users: "one live code per account" is a database
        // invariant (ix_email_verification_codes_user_id_active), so two
        // simultaneously unconsumed codes can never belong to the same user.
        await SeedEmailVerificationCodeAsync(expiredCodeUserId, now.AddHours(-1));
        var liveCodeId = await SeedEmailVerificationCodeAsync(liveCodeUserId, now.AddHours(1));

        await RunJobAsync(now);

        await using var verifyContext = CreateIdentityDbContext();
        var remainingIds = await verifyContext.EmailVerificationCodes.Select(c => c.Id).ToListAsync(TestContext.CurrentContext.CancellationToken);
        remainingIds.Should().ContainSingle().Which.Should().Be(liveCodeId);
    }

    [Test]
    public async Task Execute_DeletesExpiredOrConsumedPasswordResetTokensButKeepsLiveOnes()
    {
        var now = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
        var expiredUserId = await SeedUserAsync();
        var consumedUserId = await SeedUserAsync();
        var liveUserId = await SeedUserAsync();

        await SeedPasswordResetTokenAsync(expiredUserId, now.AddHours(-1), consumedAt: null);
        await SeedPasswordResetTokenAsync(consumedUserId, now.AddHours(1), consumedAt: now.AddMinutes(-5));
        var liveTokenId = await SeedPasswordResetTokenAsync(liveUserId, now.AddHours(1), consumedAt: null);

        await RunJobAsync(now);

        await using var verifyContext = CreateIdentityDbContext();
        var remainingIds = await verifyContext.PasswordResetTokens.Select(t => t.Id).ToListAsync(TestContext.CurrentContext.CancellationToken);
        remainingIds.Should().ContainSingle().Which.Should().Be(liveTokenId);
    }

    [Test]
    public async Task Execute_DeletesDispatchedOutboxPastRetentionButKeepsUndeliveredAndRecent()
    {
        var now = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);

        await SeedOutboxMessageAsync(now.AddDays(-10), processedAt: now.AddDays(-8));
        var undeliveredId = await SeedOutboxMessageAsync(now.AddDays(-10), processedAt: null);
        var recentlyDispatchedId = await SeedOutboxMessageAsync(now.AddDays(-1), processedAt: now.AddDays(-1));

        await RunJobAsync(now, outboxRetentionDays: 7);

        await using var verifyContext = CreateEconomyDbContext();
        var remainingIds = await verifyContext.OutboxMessages.Select(m => m.Id).ToListAsync(TestContext.CurrentContext.CancellationToken);
        remainingIds.Should().BeEquivalentTo([undeliveredId, recentlyDispatchedId]);
    }

    [Test]
    public async Task Execute_RunTwiceOnAlreadyCleanedDatabase_IsNoOpAndDoesNotThrow()
    {
        var now = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
        var userId = await SeedUserAsync();

        await SeedRefreshTokenFamilyAsync(userId, now.AddDays(-1), revokedAt: null);
        var liveFamilyId = await SeedRefreshTokenFamilyAsync(userId, now.AddDays(10), revokedAt: null);

        await RunJobAsync(now);

        var act = async () => await RunJobAsync(now);
        await act.Should().NotThrowAsync();

        await using var verifyContext = CreateIdentityDbContext();
        var remainingIds = await verifyContext.RefreshTokenFamilies.Select(f => f.Id).ToListAsync(TestContext.CurrentContext.CancellationToken);
        remainingIds.Should().ContainSingle().Which.Should().Be(liveFamilyId);
    }

    private async Task RunJobAsync(DateTimeOffset now, int outboxRetentionDays = 7)
    {
        var identityOptions = new DbContextOptionsBuilder<IdentityCleanupDbContext>().UseNpgsql(_identityConnectionString).Options;
        var economyOptions = new DbContextOptionsBuilder<EconomyCleanupDbContext>().UseNpgsql(_economyConnectionString).Options;

        var identityFactory = new TestDbContextFactory<IdentityCleanupDbContext>(() => new IdentityCleanupDbContext(identityOptions));
        var economyFactory = new TestDbContextFactory<EconomyCleanupDbContext>(() => new EconomyCleanupDbContext(economyOptions));

        var timeProvider = new FakeTimeProvider(now);
        var options = Microsoft.Extensions.Options.Options.Create(new CleanupJobOptions { OutboxRetentionDays = outboxRetentionDays });
        var job = new CleanupExpiredTokensJob(
            identityFactory, economyFactory, options, timeProvider, NullLogger<CleanupExpiredTokensJob>.Instance);

        var jobExecutionContext = Substitute.For<IJobExecutionContext>();
        jobExecutionContext.CancellationToken.Returns(TestContext.CurrentContext.CancellationToken);

        await job.Execute(jobExecutionContext);
    }

    private async Task<Guid> SeedUserAsync()
    {
        await using var dbContext = CreateIdentityDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = "Cleanup Test User",
            PasswordHash = "not-a-real-hash",
            IsActive = true,
            EmailConfirmed = true,
            EmailConfirmedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        return user.Id;
    }

    private async Task<Guid> SeedRefreshTokenFamilyAsync(Guid userId, DateTimeOffset expiresAt, DateTimeOffset? revokedAt)
    {
        await using var dbContext = CreateIdentityDbContext();
        var family = new RefreshTokenFamily
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Scope = TokenScope.Platform,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt,
        };
        dbContext.RefreshTokenFamilies.Add(family);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        return family.Id;
    }

    private async Task<Guid> SeedEmailVerificationCodeAsync(Guid userId, DateTimeOffset expiresAt)
    {
        await using var dbContext = CreateIdentityDbContext();
        var code = new EmailVerificationCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = "not-a-real-hash",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            AttemptCount = 0,
            SentToEmail = $"{Guid.NewGuid():N}@example.com",
        };
        dbContext.EmailVerificationCodes.Add(code);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        return code.Id;
    }

    private async Task<Guid> SeedPasswordResetTokenAsync(Guid userId, DateTimeOffset expiresAt, DateTimeOffset? consumedAt)
    {
        await using var dbContext = CreateIdentityDbContext();
        var token = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Guid.NewGuid().ToByteArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            ConsumedAt = consumedAt,
        };
        dbContext.PasswordResetTokens.Add(token);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        return token.Id;
    }

    private async Task<Guid> SeedOutboxMessageAsync(DateTimeOffset occurredAt, DateTimeOffset? processedAt)
    {
        await using var dbContext = CreateEconomyDbContext();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "cleanup.test",
            Version = 1,
            Payload = "{}",
            OccurredAt = occurredAt,
            ProcessedAt = processedAt,
        };
        dbContext.OutboxMessages.Add(message);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
        return message.Id;
    }

    private IdentityDbContext CreateIdentityDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(_identityConnectionString);
        return new IdentityDbContext(optionsBuilder.Options);
    }

    private EconomyDbContext CreateEconomyDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EconomyDbContext>().UseNpgsql(_economyConnectionString);
        return new EconomyDbContext(optionsBuilder.Options);
    }

    private static async Task<Respawner> CreateRespawnerAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
        });
    }

    private static async Task ResetAsync(string connectionString, Respawner respawner)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await respawner.ResetAsync(connection);
    }

    private sealed class TestDbContextFactory<TContext>(Func<TContext> factory) : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext() => factory();

        public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(factory());
    }
}
