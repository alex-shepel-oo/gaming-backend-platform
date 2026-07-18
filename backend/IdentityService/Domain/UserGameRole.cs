using IdentityService.Domain.Enums;

namespace IdentityService.Domain;

public sealed class UserGameRole
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public Guid? GameId { get; init; }
    public required PlatformRole Role { get; set; }
    public required DateTimeOffset GrantedAt { get; init; }

    public User? User { get; init; }
    public Game? Game { get; init; }
}
