using IdentityService.Auth;
using IdentityService.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IdentityService.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/users");

        group.MapPost("/{userId:guid}/revoke-sessions", RevokeSessionsAsync)
            .RequireAuthorization(Policies.Admin);
    }

    private static async Task<NoContent> RevokeSessionsAsync(
        Guid userId,
        Guid? gameId,
        ISessionService sessionService,
        CancellationToken cancellationToken)
    {
        await sessionService.RevokeAllSessionsAsync(userId, gameId, cancellationToken);

        return TypedResults.NoContent();
    }
}
