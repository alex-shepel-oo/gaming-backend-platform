using IdentityService.Auth;
using IdentityService.Domain;
using IdentityService.Domain.Enums;

namespace IdentityService.Services;

public static class DefaultRolePermissions
{
    public static IEnumerable<RolePermission> ForGame(Guid gameId, DateTimeOffset grantedAt) =>
        new[]
        {
            Permissions.GameMetadataEdit,
            Permissions.GameCurrencyManage,
            Permissions.GameBalanceAdjust,
            Permissions.GameRolesManage,
            Permissions.GamePlayersModerate,
        }.Select(permission => NewRow(PlatformRole.Admin, gameId, permission, grantedAt))
        .Concat(new[]
        {
            Permissions.GameMetadataEdit,
            Permissions.GamePlayersModerate,
            Permissions.GameBalanceAdjust,
        }.Select(permission => NewRow(PlatformRole.Moderator, gameId, permission, grantedAt)));

    private static RolePermission NewRow(PlatformRole role, Guid gameId, string permission, DateTimeOffset grantedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        Role = role,
        GameId = gameId,
        Permission = permission,
        GrantedAt = grantedAt,
        GrantedBy = null,
    };
}
