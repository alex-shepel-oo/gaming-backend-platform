using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record ResendVerificationRequest(
    [property: Required, EmailAddress] string Email,
    string? GameSlug);
