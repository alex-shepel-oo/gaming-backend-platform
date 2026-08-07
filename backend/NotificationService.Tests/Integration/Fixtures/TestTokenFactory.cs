using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace NotificationService.Tests.Integration.Fixtures;

// Mirrors the claim shape IdentityService issues (ADR-0017) without depending on
// IdentityService: NotificationService validates independently, against TestJwks's key
// pair, same as every other downstream service.
public static class TestTokenFactory
{
    private const string Issuer = "gaming-backend-platform/identity";
    private const string Audience = "gbp-player";

    private static readonly JsonWebTokenHandler Handler = new();

    public static string IssueAccessToken(Guid userId)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(TestJwks.SigningKey, SecurityAlgorithms.RsaSha256),
        };

        return Handler.CreateToken(descriptor);
    }

    // The classic RS256-to-HS256 downgrade: the attacker has only ever seen the RSA public key
    // (from the legitimate JWKS response) and signs a token by treating those public key bytes
    // as if they were a shared HMAC secret, hoping a validator that resolves a key by kid alone
    // without checking which algorithm actually signed the token, accepts it.
    public static string IssueTokenSignedAsHmacConfusionAttempt(Guid userId)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(TestJwks.PublicKeyBytesForHmacConfusionAttempt()) { KeyId = TestJwks.SigningKey.KeyId },
                SecurityAlgorithms.HmacSha256),
        };

        return Handler.CreateToken(descriptor);
    }
}
