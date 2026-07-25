namespace IdentityService.Domain;

public sealed class PasswordResetToken
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required byte[] TokenHash { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ConsumedAt { get; set; }

    public User? User { get; init; }
}
