namespace IdentityService.Domain;

public sealed class EmailVerificationCode
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public Guid? GameId { get; init; }
    public required string CodeHash { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public required int AttemptCount { get; set; }
    public required string SentToEmail { get; init; }

    public User? User { get; init; }
    public Game? Game { get; init; }
}
