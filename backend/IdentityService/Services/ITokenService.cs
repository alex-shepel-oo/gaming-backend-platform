using IdentityService.Domain;
using IdentityService.Domain.Enums;

namespace IdentityService.Services;

public interface ITokenService
{
    string IssueAccessToken(
        User user,
        Guid? gameId,
        PlatformRole? role,
        Guid familyId,
        TokenScope scope,
        IReadOnlyList<string> permissions,
        string audience);
}
