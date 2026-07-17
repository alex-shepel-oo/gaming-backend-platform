using IdentityService.Domain;
using IdentityService.Options;
using IdentityService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

public sealed class EmailVerificationService(
    IdentityDbContext dbContext,
    IVerificationCodeGenerator generator,
    IOptions<EmailVerificationOptions> options,
    TimeProvider timeProvider) : IEmailVerificationService
{
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
}
