using IdentityService.Auth;

namespace IdentityService.Services;

public interface IScopeAuthorityGuard
{
    void EnsureScopeAuthority(ICurrentUser caller, Guid? targetGameId, string platformPermission, string gamePermission);

    void EnsureCanGrant(
        ICurrentUser caller, Guid? targetGameId, IEnumerable<string> permissionsBeingGranted, string platformPermission, string gamePermission);
}
