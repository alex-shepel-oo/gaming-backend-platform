using IdentityService.Auth;
using IdentityService.Exceptions;

namespace IdentityService.Services;

public sealed class RoleEscalationGuard : IRoleEscalationGuard
{
    public void EnsureScopeAuthority(ICurrentUser caller, Guid? targetGameId)
    {
        if (targetGameId is null)
        {
            if (!caller.Perms.Contains(Permissions.PlatformRolesManage))
            {
                throw new PermissionEscalationException();
            }

            return;
        }

        var hasPlatformAuthority = caller.Perms.Contains(Permissions.PlatformRolesManage);
        var hasGameAuthority = caller.GameId == targetGameId && caller.Perms.Contains(Permissions.GameRolesManage);

        if (!hasPlatformAuthority && !hasGameAuthority)
        {
            throw new PermissionEscalationException();
        }
    }

    public void EnsureCanGrant(ICurrentUser caller, Guid? targetGameId, IEnumerable<string> permissionsBeingGranted)
    {
        EnsureScopeAuthority(caller, targetGameId);

        foreach (var permission in permissionsBeingGranted)
        {
            if (!caller.Perms.Contains(permission))
            {
                throw new PermissionEscalationException();
            }
        }
    }
}
