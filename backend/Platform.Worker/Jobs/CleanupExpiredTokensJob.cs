using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Worker.Options;
using Platform.Worker.Persistence;
using Quartz;

namespace Platform.Worker.Jobs;

// Housekeeping across identity_db and economy_db from a single job - a
// deliberate, named exception to database-per-service (ADR-0001). The
// worker never reads either database to serve a request; it only deletes
// rows that are already dead by their own service's rules (expired,
// revoked, or long since dispatched), through the narrow cleanup contexts
// above, not the services' full models.
[DisallowConcurrentExecution]
public sealed partial class CleanupExpiredTokensJob(
    IDbContextFactory<IdentityCleanupDbContext> identityDbContextFactory,
    IDbContextFactory<EconomyCleanupDbContext> economyDbContextFactory,
    IOptions<CleanupJobOptions> options,
    TimeProvider timeProvider,
    ILogger<CleanupExpiredTokensJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var now = timeProvider.GetUtcNow();

        await using var identityDbContext = await identityDbContextFactory.CreateDbContextAsync(cancellationToken);

        // Deleting the family cascades to its refresh_tokens at the
        // database FK level, so that table is never mapped here at all.
        var deletedFamilies = await identityDbContext.RefreshTokenFamilies
            .Where(f => f.ExpiresAt < now || f.RevokedAt != null)
            .ExecuteDeleteAsync(cancellationToken);

        var deletedCodes = await identityDbContext.EmailVerificationCodes
            .Where(c => c.ExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);

        var deletedPasswordResetTokens = await identityDbContext.PasswordResetTokens
            .Where(t => t.ExpiresAt < now || t.ConsumedAt != null)
            .ExecuteDeleteAsync(cancellationToken);

        await using var economyDbContext = await economyDbContextFactory.CreateDbContextAsync(cancellationToken);

        var retentionCutoff = now - TimeSpan.FromDays(options.Value.OutboxRetentionDays);
        var deletedOutboxMessages = await economyDbContext.OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < retentionCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        LogCleanupCompleted(deletedFamilies, deletedCodes, deletedPasswordResetTokens, deletedOutboxMessages);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Cleanup removed {DeletedFamilies} refresh token families, {DeletedCodes} verification codes, " +
            "{DeletedPasswordResetTokens} password reset tokens, and {DeletedOutboxMessages} outbox messages")]
    private partial void LogCleanupCompleted(
        int deletedFamilies, int deletedCodes, int deletedPasswordResetTokens, int deletedOutboxMessages);
}
