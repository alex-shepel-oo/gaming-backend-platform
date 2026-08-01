using AwesomeAssertions;
using EconomyService.Options;
using NUnit.Framework;

namespace EconomyService.Tests.Unit;

[TestFixture]
public sealed class SeedingOptionsValidationTests
{
    [Test]
    public void Defaults_EnabledIsTrue()
    {
        var options = new SeedingOptions();

        options.Enabled.Should().BeTrue();
    }
}
