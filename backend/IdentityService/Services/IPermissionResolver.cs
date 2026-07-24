using IdentityService.Domain.Enums;

namespace IdentityService.Services;

public interface IPermissionResolver
{
    Task<IReadOnlyList<string>> ResolveAsync(PlatformRole role, Guid? gameId, CancellationToken cancellationToken = default);
}
