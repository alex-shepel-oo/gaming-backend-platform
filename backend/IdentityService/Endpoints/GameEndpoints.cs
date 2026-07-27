using IdentityService.Auth;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Domain;
using IdentityService.Exceptions;
using IdentityService.Persistence;
using IdentityService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Endpoints;

public static class GameEndpoints
{
    public static void MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/games")
            .RequireAuthorization(policy => policy.RequireClaim(IdentityClaims.Perms, Permissions.PlatformGamesManage));

        group.MapGet("", ListGamesAsync);
        group.MapPost("", CreateGameAsync);
        group.MapPatch("/{id:guid}", UpdateGameAsync);

        app.MapGet("/api/identity/games/public", ListPublicGamesAsync).RequireAuthorization(Policies.Player);
    }

    private static async Task<Ok<GameDto[]>> ListGamesAsync(IdentityDbContext dbContext, CancellationToken cancellationToken)
    {
        var games = await dbContext.Games
            .OrderBy(g => g.Name)
            .Select(g => new GameDto(g.Id, g.Slug, g.Name, g.IsActive, g.CreatedAt, g.Description, g.IconUrl))
            .ToArrayAsync(cancellationToken);

        return TypedResults.Ok(games);
    }

    private static async Task<Ok<PublicGameDto[]>> ListPublicGamesAsync(IdentityDbContext dbContext, CancellationToken cancellationToken)
    {
        var games = await dbContext.Games
            .Where(g => g.IsActive)
            .OrderBy(g => g.Name)
            .Select(g => new PublicGameDto(g.Id, g.Slug, g.Name, g.Description, g.IconUrl))
            .ToArrayAsync(cancellationToken);

        return TypedResults.Ok(games);
    }

    private static async Task<Ok<GameDto>> CreateGameAsync(
        CreateGameRequest request, IdentityDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (await dbContext.Games.AnyAsync(g => g.Slug == request.Slug, cancellationToken))
        {
            throw new GameSlugAlreadyExistsException();
        }

        var game = new Game
        {
            Id = Guid.CreateVersion7(),
            Slug = request.Slug,
            Name = request.Name,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow(),
            Description = request.Description,
            IconUrl = request.IconUrl,
        };

        dbContext.Games.Add(game);
        dbContext.RolePermissions.AddRange(DefaultRolePermissions.ForGame(game.Id, timeProvider.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new GameDto(game.Id, game.Slug, game.Name, game.IsActive, game.CreatedAt, game.Description, game.IconUrl));
    }

    private static async Task<Ok<GameDto>> UpdateGameAsync(
        Guid id, UpdateGameRequest request, IdentityDbContext dbContext, CancellationToken cancellationToken)
    {
        var game = await dbContext.Games.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);

        if (game is null)
        {
            throw new GameNotFoundException();
        }

        if (request.Name is not null)
        {
            game.Name = request.Name;
        }

        if (request.IsActive is not null)
        {
            game.IsActive = request.IsActive.Value;
        }

        if (request.Description is not null)
        {
            game.Description = request.Description == string.Empty ? null : request.Description;
        }

        if (request.IconUrl is not null)
        {
            if (!UrlValidation.TryNormalize(request.IconUrl, out var normalized))
            {
                throw new InvalidIconUrlException();
            }

            game.IconUrl = normalized;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new GameDto(game.Id, game.Slug, game.Name, game.IsActive, game.CreatedAt, game.Description, game.IconUrl));
    }
}
