using System.Linq;
using AwesomeAssertions;
using IdentityService.Auth;
using IdentityService.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Xunit;
using HeaderSameSiteMode = Microsoft.Net.Http.Headers.SameSiteMode;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace IdentityService.Tests.Unit;

public class CookieAuthWriterTests
{
    private static readonly RefreshCookieOptions DefaultOptions = new()
    {
        Name = "gbp_refresh",
        Path = "/api/identity/auth",
        RequireSecure = true,
        SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
        MaxAgeDays = 14,
    };

    [Fact]
    public void WriteRefresh_SetsCookieWithConfiguredAttributes()
    {
        var writer = CreateWriter(DefaultOptions);
        var context = new DefaultHttpContext();

        writer.WriteRefresh(context.Response, "the-refresh-token");

        var cookie = GetSetCookie(context.Response);

        cookie.Name.ToString().Should().Be("gbp_refresh");
        cookie.Value.ToString().Should().Be("the-refresh-token");
        cookie.HttpOnly.Should().BeTrue();
        cookie.SameSite.Should().Be(HeaderSameSiteMode.Strict);
        cookie.Path.ToString().Should().Be("/api/identity/auth");
        cookie.MaxAge.Should().Be(TimeSpan.FromDays(14));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteRefresh_SecureMatchesRequireSecure(bool requireSecure)
    {
        var options = CloneWith(requireSecure: requireSecure);
        var writer = CreateWriter(options);
        var context = new DefaultHttpContext();

        writer.WriteRefresh(context.Response, "token");

        GetSetCookie(context.Response).Secure.Should().Be(requireSecure);
    }

    [Fact]
    public void ClearRefresh_SetsExpiredCookieWithSamePathAndSameSiteAndSecure()
    {
        var writer = CreateWriter(DefaultOptions);
        var context = new DefaultHttpContext();

        writer.ClearRefresh(context.Response);

        var cookie = GetSetCookie(context.Response);

        cookie.Name.ToString().Should().Be(DefaultOptions.Name);
        cookie.MaxAge.Should().Be(TimeSpan.Zero);
        cookie.Path.ToString().Should().Be(DefaultOptions.Path);
        cookie.SameSite.Should().Be(HeaderSameSiteMode.Strict);
        cookie.Secure.Should().BeTrue();
    }

    [Fact]
    public void ReadRefresh_ReturnsIncomingCookieValueByConfiguredName()
    {
        var writer = CreateWriter(DefaultOptions);
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "gbp_refresh=incoming-token";

        writer.ReadRefresh(context.Request).Should().Be("incoming-token");
    }

    [Fact]
    public void ReadRefresh_NoCookiePresent_ReturnsNull()
    {
        var writer = CreateWriter(DefaultOptions);
        var context = new DefaultHttpContext();

        writer.ReadRefresh(context.Request).Should().BeNull();
    }

    private static RefreshCookieOptions CloneWith(bool requireSecure) => new()
    {
        Name = DefaultOptions.Name,
        Path = DefaultOptions.Path,
        RequireSecure = requireSecure,
        SameSite = DefaultOptions.SameSite,
        MaxAgeDays = DefaultOptions.MaxAgeDays,
    };

    private static CookieAuthWriter CreateWriter(RefreshCookieOptions options) =>
        new(MsOptions.Create(options));

    private static SetCookieHeaderValue GetSetCookie(HttpResponse response) =>
        response.GetTypedHeaders().SetCookie.Single();
}
