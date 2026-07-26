using IdentityService.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace IdentityService.Auth;

public sealed class CookieAuthWriter(
    IOptions<RefreshCookieOptions> playerOptions, IOptions<AdminRefreshCookieOptions> adminOptions) : ICookieAuthWriter
{
    public void WriteRefresh(HttpResponse response, string refreshToken, ClientSurface surface)
    {
        var cookie = Resolve(surface);

        response.Cookies.Append(cookie.Name, refreshToken, BuildCookieOptions(cookie, TimeSpan.FromDays(cookie.MaxAgeDays)));
    }

    public void ClearRefresh(HttpResponse response, ClientSurface surface)
    {
        var cookie = Resolve(surface);

        response.Cookies.Append(cookie.Name, string.Empty, BuildCookieOptions(cookie, TimeSpan.Zero));
    }

    public string? ReadRefresh(HttpRequest request, ClientSurface surface) => request.Cookies[Resolve(surface).Name];

    private CookieConfig Resolve(ClientSurface surface)
    {
        if (surface == ClientSurface.Admin)
        {
            var admin = adminOptions.Value;

            return new CookieConfig(admin.Name, admin.Path, admin.HttpOnly, admin.RequireSecure, admin.SameSite, admin.MaxAgeDays);
        }

        var player = playerOptions.Value;

        return new CookieConfig(player.Name, player.Path, player.HttpOnly, player.RequireSecure, player.SameSite, player.MaxAgeDays);
    }

    private static CookieOptions BuildCookieOptions(CookieConfig cookie, TimeSpan maxAge) => new()
    {
        HttpOnly = cookie.HttpOnly,
        Secure = cookie.RequireSecure,
        SameSite = cookie.SameSite,
        Path = cookie.Path,
        MaxAge = maxAge,
    };

    private sealed record CookieConfig(
        string Name, string Path, bool HttpOnly, bool RequireSecure, SameSiteMode SameSite, int MaxAgeDays);
}
