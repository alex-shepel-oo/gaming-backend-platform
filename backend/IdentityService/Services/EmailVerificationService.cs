using BuildingBlocks.Messaging.Outbox;
using IdentityService.Domain;
using IdentityService.Exceptions;
using IdentityService.Messaging.Events;
using IdentityService.Options;
using IdentityService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

public sealed class EmailVerificationService(
    IdentityDbContext dbContext,
    IVerificationCodeGenerator generator,
    IOptions<EmailVerificationOptions> options,
    TimeProvider timeProvider,
    IOutboxWriter outboxWriter) : IEmailVerificationService
{
    private const string DefaultGameName = "Gaming Backend Platform";

    private readonly EmailVerificationOptions _options = options.Value;

    public async Task<EmailVerificationIssueResult> IssueCodeAsync(
        Guid userId,
        Guid? gameId,
        string email,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var issued = await IssueCodeCoreAsync(userId, gameId, email, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return issued;
    }

    public async Task<EmailVerificationCode> IssueAndSendCodeAsync(
        Guid userId,
        Guid? gameId,
        string email,
        string gameName,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var issued = await IssueCodeCoreAsync(userId, gameId, email, cancellationToken);

        // Written in the same transaction as the code row above -- an outbox row and the code it
        // describes either both land or neither does, rather than the old synchronous SMTP send
        // (wrapped in a 10s timeout, failures only logged) that could silently drop the email while
        // the code row itself still committed.
        await outboxWriter.WriteAsync(
            new EmailVerificationRequestedEvent
            {
                Id = Guid.CreateVersion7(),
                OccurredAt = timeProvider.GetUtcNow(),
                Email = email,
                Code = issued.RawCode,
                GameName = gameName,
                ExpiresInMinutes = _options.CodeTtlMinutes,
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return issued.Code;
    }

    private async Task<EmailVerificationIssueResult> IssueCodeCoreAsync(
        Guid userId, Guid? gameId, string email, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await dbContext.EmailVerificationCodes
            .Where(c => c.UserId == userId && c.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.ConsumedAt, now), cancellationToken);

        var rawCode = generator.Generate();

        var code = new EmailVerificationCode
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            GameId = gameId,
            CodeHash = generator.Hash(rawCode),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.CodeTtlMinutes),
            AttemptCount = 0,
            SentToEmail = email,
        };

        dbContext.EmailVerificationCodes.Add(code);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EmailVerificationIssueResult(code, rawCode);
    }

    public async Task ConfirmAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        // ToLower() here is translated to SQL lower(), matching the functional unique
        // index on lower(email) -- it never runs as a CLR string method.
#pragma warning disable CA1304, CA1311, CA1862
        var user = await dbContext.Users
            .SingleOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862

        var now = timeProvider.GetUtcNow();

        var activeCode = user is null
            ? null
            : await dbContext.EmailVerificationCodes
                .SingleOrDefaultAsync(c => c.UserId == user.Id && c.ConsumedAt == null, cancellationToken);

        if (user is null || activeCode is null || activeCode.ExpiresAt <= now)
        {
            throw new InvalidVerificationCodeException();
        }

        if (!generator.Verify(code, activeCode.CodeHash))
        {
            activeCode.AttemptCount += 1;

            if (activeCode.AttemptCount >= _options.MaxAttempts)
            {
                activeCode.ConsumedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            throw new InvalidVerificationCodeException();
        }

        activeCode.ConsumedAt = now;
        user.EmailConfirmed = true;
        user.EmailConfirmedAt = now;
        user.UpdatedAt = now;

        await outboxWriter.WriteAsync(
            new UserEmailConfirmedEvent { Id = Guid.CreateVersion7(), OccurredAt = now, UserId = user.Id },
            cancellationToken);
    }

    public async Task ResendAsync(string email, string? gameSlug, CancellationToken cancellationToken = default)
    {
        // ToLower() here is translated to SQL lower(), matching the functional unique
        // index on lower(email) -- it never runs as a CLR string method.
#pragma warning disable CA1304, CA1311, CA1862
        var user = await dbContext.Users
            .SingleOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862

        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        var lastCreatedAt = await dbContext.EmailVerificationCodes
            .Where(c => c.UserId == user.Id)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => (DateTimeOffset?)c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastCreatedAt is not null &&
            now - lastCreatedAt.Value < TimeSpan.FromSeconds(_options.ResendCooldownSeconds))
        {
            return;
        }

        var sentInLastHour = await dbContext.EmailVerificationCodes
            .CountAsync(c => c.UserId == user.Id && c.CreatedAt > now.AddHours(-1), cancellationToken);

        if (sentInLastHour >= _options.MaxResendsPerHour)
        {
            return;
        }

        var game = gameSlug is null
            ? null
            : await dbContext.Games.SingleOrDefaultAsync(g => g.Slug == gameSlug && g.IsActive, cancellationToken);

        await IssueAndSendCodeAsync(user.Id, game?.Id, user.Email, game?.Name ?? DefaultGameName, cancellationToken);
    }
}
