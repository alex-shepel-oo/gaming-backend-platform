using AwesomeAssertions;
using EconomyService.Options;
using NUnit.Framework;

namespace EconomyService.Tests.Unit;

[TestFixture]
public sealed class ApiOptionsValidationTests
{
    [Test]
    public void Defaults_ExposeOpenApiIsTrue()
    {
        var options = new ApiOptions();

        options.ExposeOpenApi.Should().BeTrue();
    }
}
