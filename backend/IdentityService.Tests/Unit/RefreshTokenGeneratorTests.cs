using AwesomeAssertions;
using IdentityService.Services;
using Xunit;

namespace IdentityService.Tests.Unit;

public class RefreshTokenGeneratorTests
{
    private readonly RefreshTokenGenerator _generator = new();

    [Fact]
    public void GenerateRaw_ProducesAtLeastFortyThreeBase64UrlCharacters()
    {
        var raw = _generator.GenerateRaw();

        raw.Length.Should().BeGreaterThanOrEqualTo(43);
    }

    [Fact]
    public void GenerateRaw_TwoCallsProduceDifferentValues()
    {
        var first = _generator.GenerateRaw();
        var second = _generator.GenerateRaw();

        first.Should().NotBe(second);
    }

    [Fact]
    public void Hash_IsDeterministicForTheSameInput()
    {
        var raw = _generator.GenerateRaw();

        var first = _generator.Hash(raw);
        var second = _generator.Hash(raw);

        first.Should().Equal(second);
    }

    [Fact]
    public void Hash_DiffersFromTheRawToken()
    {
        var raw = _generator.GenerateRaw();

        var hash = _generator.Hash(raw);

        Convert.ToBase64String(hash).Should().NotBe(raw);
    }
}
