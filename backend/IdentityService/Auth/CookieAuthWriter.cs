using IdentityService.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace IdentityService.Auth;

public sealed class CookieAuthWriter(IOptions<RefreshCookieOptions> options) : ICookieAuthWriter
{
    public void WriteRefresh(HttpResponse response, string refreshToken)
    {
        var cookie = options.Value;

        response.Cookies.Append(cookie.Name, refreshToken, BuildCookieOptions(cookie, TimeSpan.FromDays(cookie.MaxAgeDays)));
    }

    public void ClearRefresh(HttpResponse response)
    {
        var cookie = options.Value;

        response.Cookies.Append(cookie.Name, string.Empty, BuildCookieOptions(cookie, TimeSpan.Zero));
    }

    private static CookieOptions BuildCookieOptions(RefreshCookieOptions cookie, TimeSpan maxAge) => new()
    {
        HttpOnly = cookie.HttpOnly,
        Secure = cookie.RequireSecure,
        SameSite = cookie.SameSite,
        Path = cookie.Path,
        MaxAge = maxAge,
    };
}
