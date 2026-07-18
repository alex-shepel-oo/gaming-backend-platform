using IdentityService.Domain.Enums;

namespace IdentityService.Auth;

public interface ICurrentUser
{
    Guid UserId { get; }
    string Email { get; }
    Guid? GameId { get; }
    PlatformRole Role { get; }
    Guid FamilyId { get; }
    Guid Jti { get; }
    DateTimeOffset ExpiresAt { get; }
}
