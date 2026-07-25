using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace NotificationService.Tests.Integration.Fixtures;

// Mirrors the claim shape IdentityService issues (ADR-0008) without depending on
// IdentityService: NotificationService validates independently, same as every
// other downstream service.
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
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(NotificationApiFactory.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return Handler.CreateToken(descriptor);
    }
}
