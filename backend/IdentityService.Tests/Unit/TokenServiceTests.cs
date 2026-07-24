using System.Security.Claims;
using System.Text;
using AwesomeAssertions;
using IdentityService.Auth;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Options;
using IdentityService.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IdentityService.Tests.Unit;

public class TokenServiceTests
{
    private const string SigningKey = "unit-test-signing-key-at-least-32-bytes";
    private const string Issuer = "test-issuer";
    private const string Audience = "test-audience";

    private static readonly DateTimeOffset FixedInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LongExpiredInstant = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly User TestUser = new()
    {
        Id = Guid.NewGuid(),
        Email = "player@example.com",
        DisplayName = "Player One",
        PasswordHash = "irrelevant",
        IsActive = true,
        EmailConfirmed = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static readonly JsonWebTokenHandler Handler = new();

    [Fact]
    public void IssueAccessToken_IncludesAllRequiredClaims()
    {
        var service = CreateService(new FakeTimeProvider(FixedInstant));
        var familyId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        var jwt = Handler.ReadJsonWebToken(service.IssueAccessToken(
            TestUser, gameId, PlatformRole.Player, familyId, TokenScope.Game, []));

        jwt.Subject.Should().Be(TestUser.Id.ToString());
        jwt.Id.Should().NotBeNullOrEmpty();
        jwt.GetClaim(JwtRegisteredClaimNames.Email).Value.Should().Be(TestUser.Email);
        jwt.GetClaim(JwtRegisteredClaimNames.Name).Value.Should().Be(TestUser.DisplayName);
        jwt.GetClaim(IdentityClaims.GameId).Value.Should().Be(gameId.ToString());
        jwt.GetClaim(IdentityClaims.Role).Value.Should().Be(nameof(PlatformRole.Player));
        jwt.GetClaim(IdentityClaims.FamilyId).Value.Should().Be(familyId.ToString());
        jwt.GetClaim(IdentityClaims.Scope).Value.Should().Be(nameof(TokenScope.Game));
        jwt.Audiences.Should().ContainSingle().Which.Should().Be(TokenAudiences.Player);
    }

    [Fact]
    public void IssueAccessToken_ExpiresFifteenMinutesAfterIssuedAt()
    {
        var service = CreateService(new FakeTimeProvider(FixedInstant));

        var jwt = Handler.ReadJsonWebToken(
            service.IssueAccessToken(TestUser, Guid.NewGuid(), PlatformRole.Player, Guid.NewGuid(), TokenScope.Game, []));

        (jwt.ValidTo - jwt.IssuedAt).Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void IssueAccessToken_TwoCallsProduceDifferentJti()
    {
        var service = CreateService(new FakeTimeProvider(FixedInstant));

        var first = Handler.ReadJsonWebToken(
            service.IssueAccessToken(TestUser, Guid.NewGuid(), PlatformRole.Player, Guid.NewGuid(), TokenScope.Game, []));
        var second = Handler.ReadJsonWebToken(
            service.IssueAccessToken(TestUser, Guid.NewGuid(), PlatformRole.Player, Guid.NewGuid(), TokenScope.Game, []));

        first.Id.Should().NotBe(second.Id);
    }

    [Fact]
    public void IssueAccessToken_PlatformSession_OmitsGameIdClaim()
    {
        var service = CreateService(new FakeTimeProvider(FixedInstant));

        var jwt = Handler.ReadJsonWebToken(
            service.IssueAccessToken(TestUser, null, PlatformRole.Admin, Guid.NewGuid(), TokenScope.Platform, []));

        jwt.TryGetClaim(IdentityClaims.GameId, out _).Should().BeFalse();
    }

    [Fact]
    public async Task IssueAccessToken_ValidatesSuccessfullyWithTheSameKey()
    {
        var service = CreateService(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var token = service.IssueAccessToken(
            TestUser, Guid.NewGuid(), PlatformRole.Player, Guid.NewGuid(), TokenScope.Game, []);

        var result = await Handler.ValidateTokenAsync(token, ValidationParametersFor(SigningKey));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task IssueAccessToken_FailsValidationWithADifferentKey()
    {
        var service = CreateService(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var token = service.IssueAccessToken(
            TestUser, Guid.NewGuid(), PlatformRole.Player, Guid.NewGuid(), TokenScope.Game, []);

        var result = await Handler.ValidateTokenAsync(token, ValidationParametersFor("a-completely-different-32-byte-key"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task IssueAccessToken_ExpiredToken_FailsValidationDespiteClockSkew()
    {
        var service = CreateService(new FakeTimeProvider(LongExpiredInstant));
        var token = service.IssueAccessToken(
            TestUser, Guid.NewGuid(), PlatformRole.Player, Guid.NewGuid(), TokenScope.Game, []);

        var result = await Handler.ValidateTokenAsync(token, ValidationParametersFor(SigningKey));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task IssueAccessToken_SerializesPermissionsAsRepeatedClaims()
    {
        var service = CreateService(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var token = service.IssueAccessToken(
            TestUser, Guid.NewGuid(), PlatformRole.Admin, Guid.NewGuid(), TokenScope.Platform, ["a", "b", "c"]);

        var result = await Handler.ValidateTokenAsync(token, ValidationParametersFor(SigningKey));
        result.IsValid.Should().BeTrue();

        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        var permissionClaims = principal.FindAll(IdentityClaims.Perms).Select(c => c.Value).ToArray();

        permissionClaims.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    private static TokenService CreateService(TimeProvider timeProvider) => new(
        Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audiences = [Audience],
            Key = SigningKey,
            AccessTokenLifetimeMinutes = 15,
            ClockSkewSeconds = 30,
        }),
        timeProvider);

    private static TokenValidationParameters ValidationParametersFor(string key) => new()
    {
        ValidIssuer = Issuer,
        ValidAudiences = [TokenAudiences.Player],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ClockSkew = TimeSpan.FromSeconds(30),
    };
}
