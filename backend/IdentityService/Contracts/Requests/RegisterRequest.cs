using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record RegisterRequest(
    [property: Required] string GameSlug,
    [property: Required, EmailAddress] string Email,
    [property: Required, StringLength(64, MinimumLength = 2)] string DisplayName,
    [property: Required, StringLength(128, MinimumLength = 12)] string Password);
