namespace EconomyService.Auth;

public static class BalanceScopeGuard
{
    public static bool CanAdjust(ICurrentUser caller, Guid? targetGameId) =>
        caller.Perms.Contains(Permissions.PlatformBalanceAdjust)
        || (caller.Perms.Contains(Permissions.GameBalanceAdjust) && caller.GameId == targetGameId);
}
