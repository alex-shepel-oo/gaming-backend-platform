using EconomyService.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EconomyService.Tests.Integration.Fixtures;

// Builds bearer tokens shaped exactly like the ones IdentityService issues (same claim
// names, same RS256/JWKS key convention - ADR-0017) without depending on IdentityService
// itself: EconomyService validates independently, against TestJwks's key pair.
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
            SigningCredentials = new SigningCredentials(TestJwks.SigningKey, SecurityAlgorithms.RsaSha256),
        };

        return Handler.CreateToken(descriptor);
    }

    // The classic RS256-to-HS256 downgrade: the attacker has only ever seen the RSA public key
    // (from the legitimate JWKS response) and signs a token by treating those public key bytes
    // as if they were a shared HMAC secret, hoping a validator that resolves a key by kid alone
    // -- without checking which algorithm actually signed the token -- accepts it.
    public static string IssueTokenSignedAsHmacConfusionAttempt(Guid userId)
    {
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = userId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [EconomyClaims.Role] = "Player",
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(TestJwks.PublicKeyBytesForHmacConfusionAttempt()) { KeyId = TestJwks.SigningKey.KeyId },
                SecurityAlgorithms.HmacSha256),
        };

        return Handler.CreateToken(descriptor);
    }
}
