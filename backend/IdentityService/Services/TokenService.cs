using System.Text;
using IdentityService.Auth;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Services;

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly JwtOptions _options = options.Value;

    public string IssueAccessToken(
        User user, Guid? gameId, PlatformRole role, Guid familyId, TokenScope scope, IReadOnlyList<string> permissions)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),
            [JwtRegisteredClaimNames.Email] = user.Email,
            [JwtRegisteredClaimNames.Name] = user.DisplayName,
            [IdentityClaims.Role] = role.ToString(),
            [IdentityClaims.FamilyId] = familyId.ToString(),
            [IdentityClaims.Scope] = scope.ToString(),
            [IdentityClaims.Perms] = permissions.ToArray(),
        };

        if (gameId is not null)
        {
            claims[IdentityClaims.GameId] = gameId.Value.ToString();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = TokenAudiences.Player,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
                SecurityAlgorithms.HmacSha256),
        };

        return Handler.CreateToken(descriptor);
    }
}
