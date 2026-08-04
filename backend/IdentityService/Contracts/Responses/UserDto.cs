using IdentityService.Domain.Enums;

namespace IdentityService.Contracts.Responses;

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    Guid? GameId,
    string? GameSlug,
    PlatformRole? Role,
    DateTimeOffset CreatedAt,
    string? AvatarUrl,
    DateTimeOffset? LastLoginAt);
