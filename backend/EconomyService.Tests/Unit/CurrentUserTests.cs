using System.Security.Claims;
using AwesomeAssertions;
using EconomyService.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using NUnit.Framework;

namespace EconomyService.Tests.Unit;

[TestFixture]
public sealed class CurrentUserTests
{
    [Test]
    public void GameId_TokenCarriesGameIdClaim_ReturnsParsedGuid()
    {
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var currentUser = BuildCurrentUser(userId, gameId, "Player");

        currentUser.UserId.Should().Be(userId);
        currentUser.GameId.Should().Be(gameId);
        currentUser.Role.Should().Be("Player");
    }

    [Test]
    public void GameId_TokenHasNoGameIdClaim_ReturnsNull()
    {
        var currentUser = BuildCurrentUser(Guid.NewGuid(), gameId: null, "Admin");

        currentUser.GameId.Should().BeNull();
        currentUser.Role.Should().Be("Admin");
    }

    private static CurrentUser BuildCurrentUser(Guid userId, Guid? gameId, string role)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(EconomyClaims.Role, role),
        };

        if (gameId is not null)
        {
            claims.Add(new Claim(EconomyClaims.GameId, gameId.Value.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(new DefaultHttpContext { User = principal });

        return new CurrentUser(httpContextAccessor);
    }
}
