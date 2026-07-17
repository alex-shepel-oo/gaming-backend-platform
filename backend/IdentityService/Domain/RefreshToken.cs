namespace IdentityService.Domain;

public sealed class RefreshToken
{
    public required Guid Id { get; init; }
    public required Guid FamilyId { get; init; }
    public required byte[] TokenHash { get; init; }
    public required int Generation { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? CreatedByIp { get; init; }

    public RefreshTokenFamily? Family { get; init; }
    public RefreshToken? ReplacedByToken { get; init; }
}
