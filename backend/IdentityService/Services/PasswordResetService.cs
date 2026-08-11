using BuildingBlocks.Messaging.Outbox;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Exceptions;
using IdentityService.Messaging.Events;
using IdentityService.Options;
using IdentityService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

public sealed class PasswordResetService(
    IdentityDbContext dbContext,
    IRefreshTokenGenerator tokenGenerator,
    IPasswordHasher passwordHasher,
    ISessionService sessionService,
    IOutboxWriter outboxWriter,
    IOptions<PasswordResetOptions> options,
    IOptions<EmailOptions> emailOptions,
    TimeProvider timeProvider) : IPasswordResetService
{
    private readonly PasswordResetOptions _options = options.Value;
    private readonly EmailOptions _emailOptions = emailOptions.Value;

    public async Task RequestResetAsync(string email, CancellationToken cancellationToken = default)
    {
        // ToLower() here is translated to SQL lower(), matching the functional unique
        // index on lower(email): it never runs as a CLR string method.
#pragma warning disable CA1304, CA1311, CA1862
        var user = await dbContext.Users
            .SingleOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862

        if (user is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        var lastCreatedAt = await dbContext.PasswordResetTokens
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (DateTimeOffset?)t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastCreatedAt is not null &&
            now - lastCreatedAt.Value < TimeSpan.FromSeconds(_options.CooldownSeconds))
        {
            return;
        }

        var rawToken = tokenGenerator.GenerateRaw();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.ConsumedAt, now), cancellationToken);

        var token = new PasswordResetToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = tokenGenerator.Hash(rawToken),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.TokenTtlMinutes),
        };

        dbContext.PasswordResetTokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken);

        var resetLink = $"{_emailOptions.FrontendBaseUrl}/reset-password?token={rawToken}";

        // Written in the same transaction as the token row above: see EmailVerificationService's
        // own comment on IssueAndSendCodeAsync for why this replaces the old synchronous SMTP send.
        await outboxWriter.WriteAsync(
            new PasswordResetRequestedEvent
            {
                Id = Guid.CreateVersion7(),
                OccurredAt = now,
                Email = user.Email,
                ResetLink = resetLink,
                ExpiresInMinutes = _options.TokenTtlMinutes,
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ValidateTokenAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var hash = tokenGenerator.Hash(rawToken);

        var token = await dbContext.PasswordResetTokens
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        var now = timeProvider.GetUtcNow();

        if (token is null || token.ConsumedAt is not null || token.ExpiresAt <= now)
        {
            throw new InvalidPasswordResetTokenException();
        }
    }

    public async Task<string> CompleteResetAsync(string rawToken, string newPassword, CancellationToken cancellationToken = default)
    {
        var hash = tokenGenerator.Hash(rawToken);

        // No ConsumedAt filter here on purpose: we need to tell "not found" apart from
        // "already used" internally (for logging/debugging), even though every one of those
        // outcomes below throws the exact same exception to the caller.
        var token = await dbContext.PasswordResetTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        var now = timeProvider.GetUtcNow();

        if (token is null || token.ConsumedAt is not null || token.ExpiresAt <= now)
        {
            throw new InvalidPasswordResetTokenException();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        token.ConsumedAt = now;
        token.User!.PasswordHash = passwordHasher.Hash(newPassword);
        token.User.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Password change compromises the whole account, not just the game the reset was
        // requested from: revoke every refresh family across every game, not gameId-scoped.
        await sessionService.RevokeAllSessionsAsync(
            token.UserId, gameId: null, RevocationReason.PasswordChange, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return token.User.Email;
    }
}
