namespace IdentityService.Services;

public interface ISessionService
{
    Task LogoutAsync(
        Guid callerUserId,
        Guid? callerGameId,
        Guid callerJti,
        DateTimeOffset accessTokenExpiresAt,
        string rawRefreshToken,
        CancellationToken cancellationToken = default);

    Task RevokeAllSessionsAsync(Guid userId, Guid? gameId, CancellationToken cancellationToken = default);
}
