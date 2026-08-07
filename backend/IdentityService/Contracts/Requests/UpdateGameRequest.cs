using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record UpdateGameRequest(
    [property: StringLength(200, MinimumLength = 1)] string? Name,
    bool? IsActive,
    [property: StringLength(2000)] string? Description = null,
    string? IconUrl = null);
