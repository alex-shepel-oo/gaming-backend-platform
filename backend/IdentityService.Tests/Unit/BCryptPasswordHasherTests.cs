using AwesomeAssertions;
using IdentityService.Services;
using Xunit;

namespace IdentityService.Tests.Unit;

public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesHashThatVerifiesAgainstTheOriginalPassword()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        _hasher.Verify("correct horse battery staple", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_RejectsWrongPassword()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        _hasher.Verify("wrong password", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_ProducesDifferentOutputForTheSamePasswordEachTime()
    {
        var first = _hasher.Hash("same password");
        var second = _hasher.Hash("same password");

        first.Should().NotBe(second);
    }
}
