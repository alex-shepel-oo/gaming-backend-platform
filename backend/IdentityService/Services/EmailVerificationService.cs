using BuildingBlocks.Messaging.Outbox;
using IdentityService.Domain;
using IdentityService.Exceptions;
using IdentityService.Messaging.Events;
using IdentityService.Options;
using IdentityService.Persistence;
using IdentityService.Services.Email;
using IdentityService.Services.Email.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

public sealed partial class EmailVerificationService(
    IdentityDbContext dbContext,
    IVerificationCodeGenerator generator,
    IEmailSender emailSender,
    IEmailTemplateRenderer templateRenderer,
    IOptions<EmailVerificationOptions> options,
    TimeProvider timeProvider,
    IOutboxWriter outboxWriter,
    ILogger<EmailVerificationService> logger) : IEmailVerificationService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private const string DefaultGameName = "Gaming Backend Platform";

    private readonly EmailVerificationOptions _options = options.Value;

    public async Task<EmailVerificationIssueResult> IssueCodeAsync(
        Guid userId,
        Guid? gameId,
        string email,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

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

        await transaction.CommitAsync(cancellationToken);

        return new EmailVerificationIssueResult(code, rawCode);
    }

    public async Task<EmailVerificationCode> IssueAndSendCodeAsync(
        Guid userId,
        Guid? gameId,
        string email,
        string gameName,
        CancellationToken cancellationToken = default)
    {
        var issued = await IssueCodeAsync(userId, gameId, email, cancellationToken);

        try
        {
            using var timeoutCts = new CancellationTokenSource(SendTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var htmlBody = templateRenderer.RenderEmailVerification(issued.RawCode, gameName, _options.CodeTtlMinutes);
            var textBody =
                $"Confirm your email for {gameName}. Your verification code is {issued.RawCode}. " +
                $"It expires in {_options.CodeTtlMinutes} minutes.";

            await emailSender.SendAsync(
                new EmailMessage(email, "Confirm your email", htmlBody, textBody),
                linkedCts.Token);
        }
        catch (Exception exception)
        {
            LogVerificationEmailSendFailed(exception, userId);
        }

        return issued.Code;
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send verification email for user {UserId}")]
    private partial void LogVerificationEmailSendFailed(Exception exception, Guid userId);
}
