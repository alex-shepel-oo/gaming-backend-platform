using AwesomeAssertions;
using IdentityService.Services;
using Xunit;

namespace IdentityService.Tests.Unit;

public class VerificationCodeGeneratorTests
{
    private readonly VerificationCodeGenerator _generator = new(new BCryptPasswordHasher());

    [Fact]
    public void Generate_ProducesExactlySixDigitsIncludingLeadingZeros()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => _generator.Generate()).ToList();

        codes.Should().OnlyContain(code => code.Length == 6 && code.All(char.IsAsciiDigit));
    }

    [Fact]
    public void Generate_OverManyDrawsProducesAWideSpreadOfValues()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => _generator.Generate()).ToList();

        codes.Distinct().Count().Should().BeGreaterThan(800);
    }

    [Fact]
    public void Generate_NeverRepeatsTheImmediatelyPrecedingValue()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => _generator.Generate()).ToList();

        codes.Zip(codes.Skip(1), (first, second) => first != second).Should().OnlyContain(distinct => distinct);
    }

    [Fact]
    public void Hash_DiffersFromTheRawCode()
    {
        var code = _generator.Generate();

        var hash = _generator.Hash(code);

        hash.Should().NotBe(code);
    }

    [Fact]
    public void Verify_AcceptsTheMatchingCodeAndRejectsAnyOther()
    {
        var code = _generator.Generate();
        var hash = _generator.Hash(code);

        _generator.Verify(code, hash).Should().BeTrue();
        _generator.Verify("000000" == code ? "111111" : "000000", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_ProducesADifferentHashForTheSameCodeEachTime()
    {
        var code = _generator.Generate();

        var first = _generator.Hash(code);
        var second = _generator.Hash(code);

        first.Should().NotBe(second);
        _generator.Verify(code, first).Should().BeTrue();
        _generator.Verify(code, second).Should().BeTrue();
    }
}
