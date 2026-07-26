using IdentityService.Domain.Enums;

namespace IdentityService.Contracts.Responses;

public sealed record UserSummaryDto(
    Guid Id,
    string Email,
    string DisplayName,
    PlatformRole Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);
