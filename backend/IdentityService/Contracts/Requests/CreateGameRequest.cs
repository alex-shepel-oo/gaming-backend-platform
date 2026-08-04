using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record CreateGameRequest(
    [property: Required] string Slug,
    [property: Required] string Name,
    [property: StringLength(2000)] string? Description = null,
    string? IconUrl = null);
