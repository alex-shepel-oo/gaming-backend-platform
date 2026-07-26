namespace IdentityService.Auth;

public static class AccountPermissions
{
    public const string GamesList = "account.games.list";
    public const string ProfileManage = "account.profile.manage";

    public static readonly IReadOnlyList<string> All = [GamesList, ProfileManage];
}
