using Microsoft.AspNetCore.Http;

namespace IdentityService.Auth;

public interface ICookieAuthWriter
{
    void WriteRefresh(HttpResponse response, string refreshToken);

    void ClearRefresh(HttpResponse response);

    string? ReadRefresh(HttpRequest request);
}
