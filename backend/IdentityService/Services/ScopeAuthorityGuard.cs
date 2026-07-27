using IdentityService.Auth;
using IdentityService.Exceptions;

namespace IdentityService.Services;

public sealed class ScopeAuthorityGuard : IScopeAuthorityGuard
{
    public void EnsureScopeAuthority(ICurrentUser caller, Guid? targetGameId, string platformPermission, string gamePermission)
    {
        if (targetGameId is null)
        {
            if (!caller.Perms.Contains(platformPermission))
            {
                throw new PermissionEscalationException();
            }

            return;
        }

        var hasPlatformAuthority = caller.Perms.Contains(platformPermission);
        var hasGameAuthority = caller.GameId == targetGameId && caller.Perms.Contains(gamePermission);

        if (!hasPlatformAuthority && !hasGameAuthority)
        {
            throw new PermissionEscalationException();
        }
    }

    public void EnsureCanGrant(
        ICurrentUser caller, Guid? targetGameId, IEnumerable<string> permissionsBeingGranted, string platformPermission, string gamePermission)
    {
        EnsureScopeAuthority(caller, targetGameId, platformPermission, gamePermission);

        foreach (var permission in permissionsBeingGranted)
        {
            if (!caller.Perms.Contains(permission))
            {
                throw new PermissionEscalationException();
            }
        }
    }
}
