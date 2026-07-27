using System.Security.Cryptography;
using AwesomeAssertions;
using IdentityService.Options;
using IdentityService.Services;
using Xunit;

namespace IdentityService.Tests.Unit;

public class JwtSigningKeysTests
{
    // RSA-2048 test-only key pair, generated fresh each test run -- see the same
    // comment in IdentityApiFactory.cs for why this isn't a hardcoded PEM literal.
    // Written out two ways below: once with real newlines (how a Kubernetes Secret's
    // stringData block scalar delivers it) and once with literal \n escape sequences
    // (how a single-line .env value has to deliver it).
    private static readonly string PrivateKeyPemWithRealNewlines = RSA.Create(2048).ExportPkcs8PrivateKeyPem();

    private static readonly string PrivateKeyPemWithEscapedNewlines =
        PrivateKeyPemWithRealNewlines.ReplaceLineEndings("\n").Replace("\n", "\\n");

    [Fact]
    public void Constructor_AcceptsPemWithRealNewlines()
    {
        var keys = new JwtSigningKeys(
            Microsoft.Extensions.Options.Options.Create(new JwtOptions { PrivateKeyPem = PrivateKeyPemWithRealNewlines }));

        keys.SigningKey.KeyId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Constructor_NormalizesEscapedNewlines_ToTheSameKeyAsRealNewlines()
    {
        var fromRealNewlines = new JwtSigningKeys(
            Microsoft.Extensions.Options.Options.Create(new JwtOptions { PrivateKeyPem = PrivateKeyPemWithRealNewlines }));
        var fromEscapedNewlines = new JwtSigningKeys(
            Microsoft.Extensions.Options.Options.Create(new JwtOptions { PrivateKeyPem = PrivateKeyPemWithEscapedNewlines }));

        // The key id is derived from a SHA-256 hash of the public key info (see
        // JwtSigningKeys.DeriveKeyId), so an identical kid is proof the \n-escaped
        // .env-style value and the real-newline Kubernetes-style value round-trip
        // to the exact same RSA key, not merely to two keys that both happen to work.
        fromEscapedNewlines.SigningKey.KeyId.Should().Be(fromRealNewlines.SigningKey.KeyId);
    }
}
