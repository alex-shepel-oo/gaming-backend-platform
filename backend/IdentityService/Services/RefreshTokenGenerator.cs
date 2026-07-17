using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace IdentityService.Services;

public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    private const int RawTokenSizeBytes = 32;

    public string GenerateRaw() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RawTokenSizeBytes));

    public byte[] Hash(string rawToken) => SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
}
