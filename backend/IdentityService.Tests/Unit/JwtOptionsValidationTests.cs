using AwesomeAssertions;
using IdentityService.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace IdentityService.Tests.Unit;

public class JwtOptionsValidationTests
{
    [Fact]
    public async Task Host_MissingPrivateKeyPem_FailsToStartInsteadOfOnFirstRequest()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services
                .AddOptions<JwtOptions>()
                .Configure(options =>
                {
                    options.Issuer = "gaming-backend-platform/identity";
                    options.Audiences = ["gaming-backend-platform"];
                    options.PrivateKeyPem = string.Empty;
                    options.AccessTokenLifetimeMinutes = 15;
                    options.ClockSkewSeconds = 30;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart())
            .Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>();
    }
}
