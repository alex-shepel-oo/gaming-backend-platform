using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record UpdateProfileRequest(
    [property: StringLength(64, MinimumLength = 2)] string? DisplayName,
    string? AvatarUrl);
