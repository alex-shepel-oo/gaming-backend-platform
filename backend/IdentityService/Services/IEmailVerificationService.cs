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

    Task<EmailVerificationCode> IssueAndSendCodeAsync(
        Guid userId,
        Guid? gameId,
        string email,
        string gameName,
        CancellationToken cancellationToken = default);

    Task ConfirmAsync(string email, string code, CancellationToken cancellationToken = default);
}
