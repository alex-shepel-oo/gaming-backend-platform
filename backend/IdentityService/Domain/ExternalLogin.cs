namespace IdentityService.Domain;

public sealed class ExternalLogin
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required string Provider { get; init; }
    public required string ProviderUserId { get; init; }
    public required DateTimeOffset LinkedAt { get; init; }

    public User? User { get; init; }
}
