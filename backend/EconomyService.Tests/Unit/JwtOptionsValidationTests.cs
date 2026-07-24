using AwesomeAssertions;
using EconomyService.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace EconomyService.Tests.Unit;

[TestFixture]
public sealed class JwtOptionsValidationTests
{
    [Test]
    public async Task Host_KeyShorterThanMinimumLength_FailsToStartInsteadOfOnFirstRequest()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services
                .AddOptions<JwtOptions>()
                .Configure(options =>
                {
                    options.Issuer = "gaming-backend-platform/identity";
                    options.Audiences = ["gaming-backend-platform"];
                    options.Key = "too-short-key";
                    options.ClockSkewSeconds = 30;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart())
            .Build();

        var act = async () => await host.StartAsync(TestContext.CurrentContext.CancellationToken);

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Test]
    public async Task Host_EmptyKey_FailsToStartInsteadOfOnFirstRequest()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services
                .AddOptions<JwtOptions>()
                .Configure(options =>
                {
                    options.Issuer = "gaming-backend-platform/identity";
                    options.Audiences = ["gaming-backend-platform"];
                    options.Key = string.Empty;
                    options.ClockSkewSeconds = 30;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart())
            .Build();

        var act = async () => await host.StartAsync(TestContext.CurrentContext.CancellationToken);

        await act.Should().ThrowAsync<OptionsValidationException>();
    }
}
