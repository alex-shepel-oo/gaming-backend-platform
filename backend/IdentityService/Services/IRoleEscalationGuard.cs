using IdentityService.Auth;

namespace IdentityService.Services;

public interface IRoleEscalationGuard
{
    void EnsureScopeAuthority(ICurrentUser caller, Guid? targetGameId);

    void EnsureCanGrant(ICurrentUser caller, Guid? targetGameId, IEnumerable<string> permissionsBeingGranted);
}
