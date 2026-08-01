using AwesomeAssertions;
using IdentityService.Options;
using Xunit;

namespace IdentityService.Tests.Unit;

public class SeedingOptionsValidationTests
{
    [Fact]
    public void Defaults_EnabledIsTrue()
    {
        var options = new SeedingOptions();

        options.Enabled.Should().BeTrue();
    }
}
