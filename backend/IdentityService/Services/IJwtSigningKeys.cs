using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Services;

public interface IJwtSigningKeys
{
    RsaSecurityKey SigningKey { get; }

    object PublicJwks { get; }
}
