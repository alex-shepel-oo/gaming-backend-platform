using IdentityService.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace IdentityService.Auth;

public static class AuthorizationPolicies
{
    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(Policies.Player, policy => policy.RequireClaim(
            IdentityClaims.Role,
            nameof(PlatformRole.Player), nameof(PlatformRole.Moderator), nameof(PlatformRole.Admin)));

        options.AddPolicy(Policies.ModeratorOrAbove, policy => policy.RequireClaim(
            IdentityClaims.Role,
            nameof(PlatformRole.Moderator), nameof(PlatformRole.Admin)));

        options.AddPolicy(Policies.Admin, policy => policy.RequireClaim(
            IdentityClaims.Role,
            nameof(PlatformRole.Admin)));
    }
}
