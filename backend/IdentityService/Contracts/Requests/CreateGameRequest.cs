using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record CreateGameRequest(
    [property: Required, StringLength(100, MinimumLength = 1)] string Slug,
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    [property: StringLength(2000)] string? Description = null,
    string? IconUrl = null);
