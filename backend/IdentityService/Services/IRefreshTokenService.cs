using IdentityService.Domain;

namespace IdentityService.Services;

public sealed record RefreshTokenIssueResult(RefreshTokenFamily Family, RefreshToken Token, string RawToken);

public interface IRefreshTokenService
{
    Task<RefreshTokenIssueResult> IssueFamilyAsync(
        Guid userId,
        Guid? gameId,
        string? createdByIp,
        string? userAgent,
        CancellationToken cancellationToken = default);
}
