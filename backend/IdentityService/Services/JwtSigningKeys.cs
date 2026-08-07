using System.Security.Cryptography;
using IdentityService.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Services;

public sealed class JwtSigningKeys : IJwtSigningKeys
{
    public RsaSecurityKey SigningKey { get; }

    public object PublicJwks { get; }

    public JwtSigningKeys(IOptions<JwtOptions> options)
    {
        var rsa = RSA.Create();

        // A Kubernetes Secret's stringData carries the PEM as a native multi-line
        // YAML block scalar: real newlines already. A .env value is single-line
        // by format, so the same PEM arrives with literal \n escape sequences
        // instead. Normalizing here is a no-op for the former and required for the
        // latter, so both delivery mechanisms import the same way.
        var pemValue = options.Value.PrivateKeyPem.Replace("\\n", "\n");
        rsa.ImportFromPem(pemValue);

        var kid = DeriveKeyId(rsa);

        SigningKey = new RsaSecurityKey(rsa) { KeyId = kid };

        // JsonWebKeyConverter serializes whatever private components the wrapped RSA
        // instance happens to expose, so the JWKS response must be built from a
        // public-only RSA key, never straight from SigningKey, or the private
        // exponent and CRT parameters leak onto the wire.
        var publicOnlyRsa = RSA.Create();
        publicOnlyRsa.ImportParameters(rsa.ExportParameters(includePrivateParameters: false));
        var publicOnlySecurityKey = new RsaSecurityKey(publicOnlyRsa) { KeyId = kid };

        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(publicOnlySecurityKey);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;

        PublicJwks = new { keys = new[] { jwk } };
    }

    private static string DeriveKeyId(RSA rsa)
    {
        var publicKeyInfo = rsa.ExportSubjectPublicKeyInfo();
        var hash = SHA256.HashData(publicKeyInfo);

        return Convert.ToHexStringLower(hash)[..16];
    }
}
