using AwesomeAssertions;
using IdentityService.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace IdentityService.Tests.Unit;

public class RefreshCookieOptionsValidationTests
{
    [Fact]
    public async Task Host_EmptyName_FailsToStartInsteadOfOnFirstLogin()
    {
        using var host = BuildHost(options => options.Name = string.Empty);

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public async Task Host_NonPositiveMaxAgeDays_FailsToStartInsteadOfOnFirstLogin()
    {
        using var host = BuildHost(options => options.MaxAgeDays = 0);

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public void Defaults_RequireSecureIsTrue()
    {
        var options = new RefreshCookieOptions();

        options.RequireSecure.Should().BeTrue();
    }

    private static IHost BuildHost(Action<RefreshCookieOptions> configure) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services => services
                .AddOptions<RefreshCookieOptions>()
                .Configure(configure)
                .ValidateDataAnnotations()
                .ValidateOnStart())
            .Build();
}
