using IdentityService.Domain.Enums;

namespace IdentityService.Contracts.Responses;

public sealed record UserRoleDto(Guid UserId, Guid? GameId, PlatformRole Role, DateTimeOffset GrantedAt);
