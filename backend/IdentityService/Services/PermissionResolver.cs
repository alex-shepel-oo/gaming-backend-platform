using IdentityService.Domain.Enums;
using IdentityService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

public sealed class PermissionResolver(IdentityDbContext dbContext) : IPermissionResolver
{
    public async Task<IReadOnlyList<string>> ResolveAsync(
        PlatformRole role, Guid? gameId, CancellationToken cancellationToken = default) =>
        await dbContext.RolePermissions
            .Where(r => r.Role == role && r.GameId == gameId)
            .Select(r => r.Permission)
            .ToListAsync(cancellationToken);
}
