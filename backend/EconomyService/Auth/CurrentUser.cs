using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace EconomyService.Auth;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context is available to read the current user from.");

    public Guid UserId => Guid.Parse(RequireClaim(JwtRegisteredClaimNames.Sub));

    public Guid? GameId => Principal.FindFirstValue(EconomyClaims.GameId) is { } value ? Guid.Parse(value) : null;

    public string Role => RequireClaim(EconomyClaims.Role);

    public IReadOnlyList<string> Perms => Principal.FindAll(EconomyClaims.Perms).Select(c => c.Value).ToArray();

    private string RequireClaim(string claimType) =>
        Principal.FindFirstValue(claimType)
        ?? throw new InvalidOperationException($"The current principal has no '{claimType}' claim.");
}
