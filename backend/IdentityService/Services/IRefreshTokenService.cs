using IdentityService.Domain;

namespace IdentityService.Services;

public sealed record RefreshTokenIssueResult(RefreshTokenFamily Family, RefreshToken Token, string RawToken);

public sealed record RefreshRotationResult(
    string AccessToken,
    string RawRefreshToken,
    RefreshToken Token,
    RefreshTokenFamily Family);

public interface IRefreshTokenService
{
    Task<RefreshTokenIssueResult> IssueFamilyAsync(
        Guid userId,
        Guid? gameId,
        string? createdByIp,
        string? userAgent,
        CancellationToken cancellationToken = default);

    Task<RefreshRotationResult> RotateAsync(
        string rawToken,
        string? createdByIp,
        CancellationToken cancellationToken = default);
}
