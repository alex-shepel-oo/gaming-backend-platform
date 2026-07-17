using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Exceptions;
using IdentityService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

public sealed class SessionService(
    IdentityDbContext dbContext,
    IRefreshTokenGenerator generator,
    TimeProvider timeProvider) : ISessionService
{
    public async Task LogoutAsync(
        Guid callerUserId,
        Guid? callerGameId,
        Guid callerJti,
        DateTimeOffset accessTokenExpiresAt,
        string rawRefreshToken,
        CancellationToken cancellationToken = default)
    {
        var hash = generator.Hash(rawRefreshToken);

        var token = await dbContext.RefreshTokens
            .Include(t => t.Family)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is not null)
        {
            if (token.Family!.UserId != callerUserId)
            {
                throw new RefreshTokenOwnerMismatchException();
            }

            token.Family.RevokedAt ??= timeProvider.GetUtcNow();
            token.Family.RevokedReason ??= RevocationReason.Logout;
        }

        var alreadyDenied = await dbContext.RevokedAccessTokens
            .AnyAsync(r => r.Jti == callerJti, cancellationToken);

        if (!alreadyDenied)
        {
            dbContext.RevokedAccessTokens.Add(new RevokedAccessToken
            {
                Jti = callerJti,
                UserId = callerUserId,
                GameId = callerGameId,
                RevokedAt = timeProvider.GetUtcNow(),
                ExpiresAt = accessTokenExpiresAt,
                Reason = RevocationReason.Logout,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllSessionsAsync(Guid userId, Guid? gameId, CancellationToken cancellationToken = default)
    {
        var userExists = await dbContext.Users.AnyAsync(u => u.Id == userId, cancellationToken);

        if (!userExists)
        {
            throw new UserNotFoundException();
        }

        var now = timeProvider.GetUtcNow();

        var families = dbContext.RefreshTokenFamilies
            .Where(f => f.UserId == userId && f.RevokedAt == null);

        if (gameId is not null)
        {
            families = families.Where(f => f.GameId == gameId);
        }

        await families.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(f => f.RevokedAt, now)
                .SetProperty(f => f.RevokedReason, RevocationReason.AdminRevoke),
            cancellationToken);
    }
}
