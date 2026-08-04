using Microsoft.AspNetCore.Http;

namespace IdentityService.Auth;

public static class ClientMode
{
    public const string HeaderName = "X-Client-Type";

    private const string WebValue = "web";
    private const string AdminValue = "admin";

    public static ClientSurface Resolve(HttpContext httpContext)
    {
        var value = httpContext.Request.Headers[HeaderName].ToString();

        if (string.Equals(value, WebValue, StringComparison.OrdinalIgnoreCase))
        {
            return ClientSurface.Player;
        }

        if (string.Equals(value, AdminValue, StringComparison.OrdinalIgnoreCase))
        {
            return ClientSurface.Admin;
        }

        return ClientSurface.None;
    }

    public static bool IsWeb(HttpContext httpContext) => Resolve(httpContext) != ClientSurface.None;

    public static string ResolveAudience(HttpContext httpContext) =>
        Resolve(httpContext) == ClientSurface.Admin ? TokenAudiences.Admin : TokenAudiences.Player;
}
