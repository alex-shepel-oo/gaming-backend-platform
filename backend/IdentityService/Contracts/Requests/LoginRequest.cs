using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record LoginRequest(
    string? GameSlug,
    [property: Required, EmailAddress] string Email,
    [property: Required, StringLength(128)] string Password);
