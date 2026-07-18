using Microsoft.AspNetCore.Http;

namespace IdentityService.Auth;

public static class ClientMode
{
    public const string HeaderName = "X-Client-Type";

    private const string WebValue = "web";

    public static bool IsWeb(HttpContext httpContext) =>
        string.Equals(httpContext.Request.Headers[HeaderName].ToString(), WebValue, StringComparison.OrdinalIgnoreCase);
}
