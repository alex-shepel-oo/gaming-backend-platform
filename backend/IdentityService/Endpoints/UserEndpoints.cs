using System.ComponentModel.DataAnnotations;
using IdentityService.Auth;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Domain.Enums;
using IdentityService.Exceptions;
using IdentityService.Persistence;
using IdentityService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/users");

        group.MapGet("/me", GetMeAsync).RequireAuthorization();
        group.MapPatch("/me", UpdateMeAsync).RequireAuthorization();
        group.MapGet("/me/games", GetMyGamesAsync).RequireAuthorization();
        group.MapGet("/{userId:guid}", GetUserAsync).RequireAuthorization(Policies.ModeratorOrAbove);
        group.MapGet("", ListUsersAsync).RequireAuthorization(Policies.ModeratorOrAbove);

        group.MapPost("/{userId:guid}/revoke-sessions", RevokeSessionsAsync)
            .RequireAuthorization(Policies.Admin);
    }

    private static async Task<Ok<UserDto>> GetMeAsync(
        ICurrentUser currentUser, IdentityDbContext dbContext, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleAsync(u => u.Id == currentUser.UserId, cancellationToken);

        return TypedResults.Ok(new UserDto(
            user.Id, user.Email, user.DisplayName, currentUser.GameId, currentUser.Role, user.CreatedAt,
            user.AvatarUrl, user.LastLoginAt));
    }

    private static async Task<Ok<UserDto>> UpdateMeAsync(
        UpdateProfileRequest request,
        ICurrentUser currentUser,
        IdentityDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleAsync(u => u.Id == currentUser.UserId, cancellationToken);

        if (request.DisplayName is not null)
        {
            user.DisplayName = request.DisplayName;
        }

        if (request.AvatarUrl is not null)
        {
            if (!UrlValidation.TryNormalize(request.AvatarUrl, out var normalized))
            {
                throw new InvalidAvatarUrlException();
            }

            user.AvatarUrl = normalized;
        }

        user.UpdatedAt = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new UserDto(
            user.Id, user.Email, user.DisplayName, currentUser.GameId, currentUser.Role, user.CreatedAt,
            user.AvatarUrl, user.LastLoginAt));
    }

    private static async Task<Ok<PublicGameDto[]>> GetMyGamesAsync(
        ICurrentUser currentUser, IdentityDbContext dbContext, CancellationToken cancellationToken)
    {
        var games = await dbContext.UserGameRoles
            .Where(r => r.UserId == currentUser.UserId && r.GameId != null)
            .Select(r => r.Game!)
            .Distinct()
            .OrderBy(g => g.Name)
            .Select(g => new PublicGameDto(g.Id, g.Slug, g.Name, g.Description, g.IconUrl))
            .ToArrayAsync(cancellationToken);

        return TypedResults.Ok(games);
    }

    private static async Task<Ok<UserDto>> GetUserAsync(
        Guid userId, ICurrentUser currentUser, IdentityDbContext dbContext, CancellationToken cancellationToken)
    {
        var role = await dbContext.UserGameRoles
            .Include(r => r.User)
            .SingleOrDefaultAsync(r => r.UserId == userId && r.GameId == currentUser.GameId, cancellationToken);

        if (role is null)
        {
            throw new UserNotFoundException();
        }

        return TypedResults.Ok(new UserDto(
            role.User!.Id, role.User.Email, role.User.DisplayName, role.GameId, role.Role, role.User.CreatedAt,
            role.User.AvatarUrl, role.User.LastLoginAt));
    }

    private static async Task<Ok<PagedResult<UserSummaryDto>>> ListUsersAsync(
        string? search,
        ICurrentUser currentUser,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken,
        [Range(1, int.MaxValue)] int page = 1,
        [Range(1, 100)] int pageSize = 20)
    {
        var query = dbContext.UserGameRoles.Where(r => r.GameId == currentUser.GameId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(r =>
                EF.Functions.ILike(r.User!.Email, pattern) || EF.Functions.ILike(r.User!.DisplayName, pattern));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(r => r.User!.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new UserSummaryDto(r.User!.Id, r.User.Email, r.User.DisplayName, r.Role, r.User.CreatedAt, r.User.LastLoginAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResult<UserSummaryDto>(items, page, pageSize, totalCount));
    }

    private static async Task<NoContent> RevokeSessionsAsync(
        Guid userId,
        Guid? gameId,
        ISessionService sessionService,
        CancellationToken cancellationToken)
    {
        await sessionService.RevokeAllSessionsAsync(userId, gameId, RevocationReason.AdminRevoke, cancellationToken);

        return TypedResults.NoContent();
    }
}
