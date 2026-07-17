using IdentityService.Domain;
using IdentityService.Options;
using IdentityService.Persistence;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

public sealed class RefreshTokenService(
    IdentityDbContext dbContext,
    IRefreshTokenGenerator generator,
    IOptions<RefreshTokenOptions> options,
    TimeProvider timeProvider) : IRefreshTokenService
{
    private readonly RefreshTokenOptions _options = options.Value;

    public async Task<RefreshTokenIssueResult> IssueFamilyAsync(
        Guid userId,
        Guid? gameId,
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
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.FamilyAbsoluteLifetimeDays),
            CreatedByIp = createdByIp,
            UserAgent = userAgent,
        };

        var rawToken = generator.GenerateRaw();
        var slidingExpiry = now.AddDays(_options.LifetimeDays);

        var token = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            FamilyId = family.Id,
            TokenHash = generator.Hash(rawToken),
            Generation = 1,
            CreatedAt = now,
            ExpiresAt = slidingExpiry < family.ExpiresAt ? slidingExpiry : family.ExpiresAt,
            CreatedByIp = createdByIp,
        };

        dbContext.RefreshTokenFamilies.Add(family);
        dbContext.RefreshTokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RefreshTokenIssueResult(family, token, rawToken);
    }
}
