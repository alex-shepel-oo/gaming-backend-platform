using IdentityService.Auth;
using IdentityService.Contracts.Responses;
using IdentityService.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Endpoints;

public static class GameEndpoints
{
    public static void MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/identity/games", ListGamesAsync).RequireAuthorization(Policies.Admin);
        app.MapGet("/api/identity/games/public", ListPublicGamesAsync).RequireAuthorization(Policies.Player);
    }

    private static async Task<Ok<GameDto[]>> ListGamesAsync(IdentityDbContext dbContext, CancellationToken cancellationToken)
    {
        var games = await dbContext.Games
            .OrderBy(g => g.Name)
            .Select(g => new GameDto(g.Id, g.Slug, g.Name, g.IsActive, g.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return TypedResults.Ok(games);
    }

    private static async Task<Ok<PublicGameDto[]>> ListPublicGamesAsync(IdentityDbContext dbContext, CancellationToken cancellationToken)
    {
        var games = await dbContext.Games
            .Where(g => g.IsActive)
            .OrderBy(g => g.Name)
            .Select(g => new PublicGameDto(g.Id, g.Slug, g.Name))
            .ToArrayAsync(cancellationToken);

        return TypedResults.Ok(games);
    }
}
