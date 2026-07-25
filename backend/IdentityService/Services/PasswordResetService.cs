using IdentityService.Domain;
using IdentityService.Options;
using IdentityService.Persistence;
using IdentityService.Services.Email;
using IdentityService.Services.Email.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

public sealed partial class PasswordResetService(
    IdentityDbContext dbContext,
    IRefreshTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    IEmailTemplateRenderer templateRenderer,
    IOptions<PasswordResetOptions> options,
    IOptions<EmailOptions> emailOptions,
    TimeProvider timeProvider,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private readonly PasswordResetOptions _options = options.Value;
    private readonly EmailOptions _emailOptions = emailOptions.Value;

    public async Task RequestResetAsync(string email, CancellationToken cancellationToken = default)
    {
        // ToLower() here is translated to SQL lower(), matching the functional unique
        // index on lower(email) -- it never runs as a CLR string method.
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

        await transaction.CommitAsync(cancellationToken);

        try
        {
            using var timeoutCts = new CancellationTokenSource(SendTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var resetLink = $"{_emailOptions.FrontendBaseUrl}/reset-password?token={rawToken}";
            var htmlBody = templateRenderer.RenderPasswordReset(resetLink, _options.TokenTtlMinutes);
            var textBody =
                $"We received a request to reset your password. Use this link: {resetLink}. " +
                $"It expires in {_options.TokenTtlMinutes} minutes.";

            await emailSender.SendAsync(
                new EmailMessage(user.Email, "Reset your password", htmlBody, textBody),
                linkedCts.Token);
        }
        catch (Exception exception)
        {
            LogPasswordResetEmailSendFailed(exception, user.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send password reset email for user {UserId}")]
    private partial void LogPasswordResetEmailSendFailed(Exception exception, Guid userId);
}
