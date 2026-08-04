using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace EconomyService.Tests.Integration.Fixtures;

// A single RSA key pair, generated once per test run and shared by every fixture that needs to
// look like Identity's real RS256/JWKS pipeline: TestTokenFactory signs with it, and the fake
// JWKS responses handed to JwksKeyCache during tests describe its public half. Test-only key
// material generated at runtime -- nothing here is a production secret.
public static class TestJwks
{
    private const string KeyId = "economy-tests-key-1";

    private static readonly RSA Rsa = RSA.Create(2048);

    public static readonly RsaSecurityKey SigningKey = new(Rsa) { KeyId = KeyId };

    public static readonly string JwksJson = BuildJwksJson();

    // The RS256-to-HS256 downgrade attack doesn't need a different JWKS response -- the
    // legitimate public key published above is exactly what the attacker is assumed to already
    // have. What makes it an attack is the forged token treating those same public key bytes as
    // an HMAC secret instead of an RSA key; TestTokenFactory builds that token shape.
    public static byte[] PublicKeyBytesForHmacConfusionAttempt() =>
        Rsa.ExportSubjectPublicKeyInfo();

    private static string BuildJwksJson()
    {
        var publicOnlyRsa = RSA.Create();
        publicOnlyRsa.ImportParameters(Rsa.ExportParameters(includePrivateParameters: false));
        var publicKey = new RsaSecurityKey(publicOnlyRsa) { KeyId = KeyId };

        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(publicKey);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;

        return JsonSerializer.Serialize(new { keys = new[] { jwk } });
    }
}
