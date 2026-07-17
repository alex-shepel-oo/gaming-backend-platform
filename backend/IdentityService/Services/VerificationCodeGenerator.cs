using System.Globalization;
using System.Security.Cryptography;

namespace IdentityService.Services;

public sealed class VerificationCodeGenerator(IPasswordHasher passwordHasher) : IVerificationCodeGenerator
{
    private const int ExclusiveUpperBound = 1_000_000;

    public string Generate() =>
        RandomNumberGenerator.GetInt32(0, ExclusiveUpperBound).ToString("D6", CultureInfo.InvariantCulture);

    public string Hash(string code) => passwordHasher.Hash(code);

    public bool Verify(string code, string hash) => passwordHasher.Verify(code, hash);
}
