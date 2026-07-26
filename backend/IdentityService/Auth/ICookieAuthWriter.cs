using Microsoft.AspNetCore.Http;

namespace IdentityService.Auth;

public interface ICookieAuthWriter
{
    void WriteRefresh(HttpResponse response, string refreshToken, ClientSurface surface);

    void ClearRefresh(HttpResponse response, ClientSurface surface);

    string? ReadRefresh(HttpRequest request, ClientSurface surface);
}
