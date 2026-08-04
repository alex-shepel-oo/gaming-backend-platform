using System.Globalization;
using System.Security.Claims;
using IdentityService.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace IdentityService.Auth;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context is available to read the current user from.");

    public Guid UserId => Guid.Parse(RequireClaim(JwtRegisteredClaimNames.Sub));

    public string Email => RequireClaim(JwtRegisteredClaimNames.Email);

    public Guid? GameId => Principal.FindFirstValue(IdentityClaims.GameId) is { } value ? Guid.Parse(value) : null;

    public PlatformRole? Role =>
        Principal.FindFirstValue(IdentityClaims.Role) is { } value ? Enum.Parse<PlatformRole>(value) : null;

    public Guid FamilyId => Guid.Parse(RequireClaim(IdentityClaims.FamilyId));

    public Guid Jti => Guid.Parse(RequireClaim(JwtRegisteredClaimNames.Jti));

    public DateTimeOffset ExpiresAt =>
        DateTimeOffset.FromUnixTimeSeconds(long.Parse(RequireClaim(JwtRegisteredClaimNames.Exp), CultureInfo.InvariantCulture));

    public IReadOnlyList<string> Perms => Principal.FindAll(IdentityClaims.Perms).Select(c => c.Value).ToArray();

    private string RequireClaim(string claimType) =>
        Principal.FindFirstValue(claimType)
        ?? throw new InvalidOperationException($"The current principal has no '{claimType}' claim.");
}
