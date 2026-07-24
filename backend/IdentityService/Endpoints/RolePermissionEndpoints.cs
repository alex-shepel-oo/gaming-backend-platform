using System.Reflection;
using IdentityService.Auth;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Exceptions;
using IdentityService.Persistence;
using IdentityService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Endpoints;

public static class RolePermissionEndpoints
{
    private static readonly string[] PermissionCatalog = typeof(Permissions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (string)field.GetValue(null)!)
        .ToArray();

    public static void MapRolePermissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity");

        group.MapGet("/permissions", GetPermissionCatalog).RequireAuthorization(Policies.ModeratorOrAbove);

        group.MapGet("/roles/{role}/permissions", GetRolePermissionsAsync).RequireAuthorization();
        group.MapPut("/roles/{role}/permissions", UpdateRolePermissionsAsync).RequireAuthorization();

        group.MapGet("/users/{userId:guid}/roles", GetUserRoleAsync).RequireAuthorization();
        group.MapPatch("/users/{userId:guid}/roles", AssignUserRoleAsync).RequireAuthorization();
    }

    private static Ok<string[]> GetPermissionCatalog() => TypedResults.Ok(PermissionCatalog);

    private static async Task<Ok<string[]>> GetRolePermissionsAsync(
        PlatformRole role,
        Guid? gameId,
        ICurrentUser currentUser,
        IRoleEscalationGuard guard,
        IPermissionResolver permissionResolver,
        CancellationToken cancellationToken)
    {
        guard.EnsureScopeAuthority(currentUser, gameId);

        var permissions = await permissionResolver.ResolveAsync(role, gameId, cancellationToken);

        return TypedResults.Ok(permissions.ToArray());
    }

    private static async Task<Ok<string[]>> UpdateRolePermissionsAsync(
        PlatformRole role,
        Guid? gameId,
        UpdateRolePermissionsRequest request,
        ICurrentUser currentUser,
        IRoleEscalationGuard guard,
        IdentityDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        guard.EnsureCanGrant(currentUser, gameId, request.Permissions);

        var existing = await dbContext.RolePermissions
            .Where(r => r.Role == role && r.GameId == gameId)
            .ToListAsync(cancellationToken);

        dbContext.RolePermissions.RemoveRange(existing);

        var now = timeProvider.GetUtcNow();
        dbContext.RolePermissions.AddRange(request.Permissions.Select(permission => new RolePermission
        {
            Id = Guid.CreateVersion7(),
            Role = role,
            GameId = gameId,
            Permission = permission,
            GrantedAt = now,
            GrantedBy = currentUser.UserId,
        }));

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(request.Permissions);
    }

    private static async Task<Ok<UserRoleDto>> GetUserRoleAsync(
        Guid userId,
        Guid? gameId,
        ICurrentUser currentUser,
        IRoleEscalationGuard guard,
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        guard.EnsureScopeAuthority(currentUser, gameId);

        var role = await dbContext.UserGameRoles
            .SingleOrDefaultAsync(r => r.UserId == userId && r.GameId == gameId, cancellationToken);

        if (role is null)
        {
            throw new UserNotFoundException();
        }

        return TypedResults.Ok(new UserRoleDto(role.UserId, role.GameId, role.Role, role.GrantedAt));
    }

    private static async Task<Ok<UserRoleDto>> AssignUserRoleAsync(
        Guid userId,
        AssignUserRoleRequest request,
        ICurrentUser currentUser,
        IRoleEscalationGuard guard,
        IPermissionResolver permissionResolver,
        IdentityDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var effectivePermissions = await permissionResolver.ResolveAsync(request.Role, request.GameId, cancellationToken);
        guard.EnsureCanGrant(currentUser, request.GameId, effectivePermissions);

        var existing = await dbContext.UserGameRoles
            .SingleOrDefaultAsync(r => r.UserId == userId && r.GameId == request.GameId, cancellationToken);

        var now = timeProvider.GetUtcNow();

        if (existing is null)
        {
            existing = new UserGameRole
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                GameId = request.GameId,
                Role = request.Role,
                GrantedAt = now,
            };
            dbContext.UserGameRoles.Add(existing);
        }
        else
        {
            existing.Role = request.Role;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new UserRoleDto(existing.UserId, existing.GameId, existing.Role, existing.GrantedAt));
    }
}
