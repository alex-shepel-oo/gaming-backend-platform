using IdentityService.Domain.Enums;

namespace IdentityService.Domain;

public sealed class RefreshTokenFamily
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public Guid? GameId { get; init; }
    public required TokenScope Scope { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public RevocationReason? RevokedReason { get; set; }
    public string? CreatedByIp { get; init; }
    public string? UserAgent { get; init; }

    public User? User { get; init; }
    public Game? Game { get; init; }
    public ICollection<RefreshToken> RefreshTokens { get; init; } = [];
}
