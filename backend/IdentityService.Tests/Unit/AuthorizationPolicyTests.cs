using System.Security.Claims;
using AwesomeAssertions;
using IdentityService.Auth;
using IdentityService.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Unit;

public class AuthorizationPolicyTests
{
    private static readonly IAuthorizationService AuthorizationService = BuildAuthorizationService();

    [Theory]
    [InlineData(PlatformRole.Player, Policies.Player, true)]
    [InlineData(PlatformRole.Moderator, Policies.Player, true)]
    [InlineData(PlatformRole.Admin, Policies.Player, true)]
    [InlineData(PlatformRole.Player, Policies.ModeratorOrAbove, false)]
    [InlineData(PlatformRole.Moderator, Policies.ModeratorOrAbove, true)]
    [InlineData(PlatformRole.Admin, Policies.ModeratorOrAbove, true)]
    [InlineData(PlatformRole.Player, Policies.Admin, false)]
    [InlineData(PlatformRole.Moderator, Policies.Admin, false)]
    [InlineData(PlatformRole.Admin, Policies.Admin, true)]
    public async Task AuthorizeAsync_EvaluatesPolicyAgainstRoleClaim(PlatformRole role, string policy, bool expectedSucceeded)
    {
        var principal = PrincipalWithRole(role);

        var result = await AuthorizationService.AuthorizeAsync(principal, policy);

        result.Succeeded.Should().Be(expectedSucceeded);
    }

    [Theory]
    [InlineData(Policies.Player)]
    [InlineData(Policies.ModeratorOrAbove)]
    [InlineData(Policies.Admin)]
    public async Task AuthorizeAsync_PrincipalWithoutRoleClaim_Fails(string policy)
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await AuthorizationService.AuthorizeAsync(anonymous, policy);

        result.Succeeded.Should().BeFalse();
    }

    private static ClaimsPrincipal PrincipalWithRole(PlatformRole role)
    {
        var identity = new ClaimsIdentity([new Claim(IdentityClaims.Role, role.ToString())], "Test");

        return new ClaimsPrincipal(identity);
    }

    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(AuthorizationPolicies.Configure);

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }
}
