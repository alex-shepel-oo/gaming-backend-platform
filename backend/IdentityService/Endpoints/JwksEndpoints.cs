using IdentityService.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IdentityService.Endpoints;

public static class JwksEndpoints
{
    public static void MapJwksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/jwks.json", GetJwks);
    }

    private static Ok<object> GetJwks(IJwtSigningKeys signingKeys) => TypedResults.Ok(signingKeys.PublicJwks);
}
