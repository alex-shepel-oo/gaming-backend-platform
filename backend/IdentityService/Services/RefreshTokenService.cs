using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Exceptions;
using IdentityService.Options;
using IdentityService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

public sealed partial class RefreshTokenService(
    IdentityDbContext dbContext,
    IRefreshTokenGenerator generator,
    ITokenService tokenService,
    IPermissionResolver permissionResolver,
    IOptions<RefreshTokenOptions> options,
    TimeProvider timeProvider,
    ILogger<RefreshTokenService> logger) : IRefreshTokenService
{
    private readonly RefreshTokenOptions _options = options.Value;

    public async Task<RefreshTokenIssueResult> IssueFamilyAsync(
        Guid userId,
        Guid? gameId,
        TokenScope scope,
        string? createdByIp,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        var family = new RefreshTokenFamily
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            GameId = gameId,
            Scope = scope,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.FamilyAbsoluteLifetimeDays),
            CreatedByIp = createdByIp,
            UserAgent = userAgent,
        };

        var rawToken = generator.GenerateRaw();

        var token = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            FamilyId = family.Id,
            TokenHash = generator.Hash(rawToken),
            Generation = 1,
            CreatedAt = now,
            ExpiresAt = SlidingExpiry(now, family.ExpiresAt),
            CreatedByIp = createdByIp,
        };

        dbContext.RefreshTokenFamilies.Add(family);
        dbContext.RefreshTokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RefreshTokenIssueResult(family, token, rawToken);
    }

    public async Task<RefreshRotationResult> RotateAsync(
        string rawToken,
        string? createdByIp,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var hash = generator.Hash(rawToken);

        var token = await dbContext.RefreshTokens
            .Include(t => t.Family)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            throw new InvalidRefreshTokenException();
        }

        var family = token.Family!;

        if (family.RevokedAt is not null || family.ExpiresAt <= now)
        {
            throw new InvalidRefreshTokenException();
        }

        if (token.UsedAt is not null || token.RevokedAt is not null)
        {
            await RevokeFamilyForReuseAsync(family, token.Generation, cancellationToken);

            throw new InvalidRefreshTokenException();
        }

        if (token.ExpiresAt <= now)
        {
            throw new InvalidRefreshTokenException();
        }

        var user = await dbContext.Users.SingleAsync(u => u.Id == family.UserId, cancellationToken);

        if (!user.IsActive)
        {
            family.RevokedAt = now;
            family.RevokedReason = RevocationReason.UserDeactivated;
            await dbContext.SaveChangesAsync(cancellationToken);

            throw new InvalidRefreshTokenException();
        }

        var role = await dbContext.UserGameRoles
            .SingleAsync(r => r.UserId == family.UserId && r.GameId == family.GameId, cancellationToken);

        var newTokenId = Guid.CreateVersion7();
        var newRawToken = generator.GenerateRaw();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        
        var newToken = new RefreshToken
        {
            Id = newTokenId,
            FamilyId = family.Id,
            TokenHash = generator.Hash(newRawToken),
            Generation = token.Generation + 1,
            CreatedAt = now,
            ExpiresAt = SlidingExpiry(now, family.ExpiresAt),
            CreatedByIp = createdByIp,
        };

        dbContext.RefreshTokens.Add(newToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var affectedRows = await dbContext.RefreshTokens
            .Where(t => t.Id == token.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.UsedAt, now)
                    .SetProperty(t => t.ReplacedByTokenId, newTokenId),
                cancellationToken);

        if (affectedRows != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RevokeFamilyForReuseAsync(family, token.Generation, cancellationToken);

            throw new InvalidRefreshTokenException();
        }

        await transaction.CommitAsync(cancellationToken);

        var scope = family.GameId is null ? TokenScope.Platform : TokenScope.Game;
        var permissions = await permissionResolver.ResolveAsync(role.Role, family.GameId, cancellationToken);
        var accessToken = tokenService.IssueAccessToken(user, family.GameId, role.Role, family.Id, scope, permissions);

        return new RefreshRotationResult(accessToken, newRawToken, newToken, family);
    }

    private DateTimeOffset SlidingExpiry(DateTimeOffset now, DateTimeOffset familyExpiresAt)
    {
        var candidate = now.AddDays(_options.LifetimeDays);

        return candidate < familyExpiresAt ? candidate : familyExpiresAt;
    }

    private async Task RevokeFamilyForReuseAsync(
        RefreshTokenFamily family, int generation, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.RefreshTokens
            .Where(t => t.FamilyId == family.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), cancellationToken);

        family.RevokedAt = now;
        family.RevokedReason = RevocationReason.TokenReuse;
        await dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        LogRefreshTokenReuseDetected(family.Id, family.UserId, generation);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Refresh token reuse detected for family {FamilyId} (user {UserId}, generation {Generation})")]
    private partial void LogRefreshTokenReuseDetected(Guid familyId, Guid userId, int generation);
}
