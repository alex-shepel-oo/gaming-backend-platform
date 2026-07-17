namespace IdentityService.Domain.Enums;

public enum RevocationReason
{
    Logout = 0,
    TokenReuse = 1,
    AdminRevoke = 2,
    PasswordChange = 3,
    UserDeactivated = 4,
}
