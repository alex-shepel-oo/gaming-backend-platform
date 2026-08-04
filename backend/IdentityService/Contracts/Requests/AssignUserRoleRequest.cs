using IdentityService.Domain.Enums;

namespace IdentityService.Contracts.Requests;

public sealed record AssignUserRoleRequest(Guid? GameId, PlatformRole Role);
