using IdentityService.Domain;

namespace IdentityService.Services;

public sealed record EmailVerificationIssueResult(EmailVerificationCode Code, string RawCode);

public interface IEmailVerificationService
{
    Task<EmailVerificationIssueResult> IssueCodeAsync(
        Guid userId,
        Guid? gameId,
        string email,
        CancellationToken cancellationToken = default);
}
