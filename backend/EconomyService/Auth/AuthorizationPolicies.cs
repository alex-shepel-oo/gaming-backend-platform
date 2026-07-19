using Microsoft.AspNetCore.Authorization;

namespace EconomyService.Auth;

public static class AuthorizationPolicies
{
    public static void Configure(AuthorizationOptions options)
    {
        // Role names must match IdentityService's PlatformRole.ToString() output
        // (Player/Moderator/Admin) - the two services share no code (ADR-0001),
        // only the claim shape the token carries.
        options.AddPolicy(Policies.ModeratorOrAbove, policy => policy.RequireClaim(
            EconomyClaims.Role, "Moderator", "Admin"));
        options.AddPolicy(Policies.Admin, policy => policy.RequireClaim(EconomyClaims.Role, "Admin"));
    }
}
