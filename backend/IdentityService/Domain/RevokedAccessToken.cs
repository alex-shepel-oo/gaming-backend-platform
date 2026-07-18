using IdentityService.Domain.Enums;

namespace IdentityService.Domain;

public sealed class RevokedAccessToken
{
    public required Guid Jti { get; init; }
    public required Guid UserId { get; init; }
    public Guid? GameId { get; init; }
    public required DateTimeOffset RevokedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required RevocationReason Reason { get; init; }
}
