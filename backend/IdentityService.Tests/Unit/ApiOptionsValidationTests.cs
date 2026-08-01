using AwesomeAssertions;
using IdentityService.Options;
using Xunit;

namespace IdentityService.Tests.Unit;

public class ApiOptionsValidationTests
{
    [Fact]
    public void Defaults_ExposeOpenApiIsTrue()
    {
        var options = new ApiOptions();

        options.ExposeOpenApi.Should().BeTrue();
    }
}
