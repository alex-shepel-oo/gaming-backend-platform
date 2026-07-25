using System.Text;
using EconomyService.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EconomyService.Tests.Integration.Fixtures;

// Builds bearer tokens shaped exactly like the ones IdentityService issues
// (same claim names, same HS256 key convention - ADR-0008) without depending
// on IdentityService itself: EconomyService validates independently.
public static class TestTokenFactory
{
    private const string Issuer = "gaming-backend-platform/identity";
    private const string Audience = "gbp-player";

    private static readonly JsonWebTokenHandler Handler = new();

    public static string IssueAccessToken(
        Guid userId, Guid? gameId = null, string role = "Player", IReadOnlyList<string>? perms = null)
    {
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = userId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [EconomyClaims.Role] = role,
        };

        if (gameId is not null)
        {
            claims[EconomyClaims.GameId] = gameId.Value.ToString();
        }

        if (perms is { Count: > 0 })
        {
            claims[EconomyClaims.Perms] = perms.ToArray();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(EconomyApiFactory.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return Handler.CreateToken(descriptor);
    }
}
