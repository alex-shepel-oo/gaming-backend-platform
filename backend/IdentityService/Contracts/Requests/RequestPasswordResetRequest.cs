using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record RequestPasswordResetRequest([property: Required, EmailAddress] string Email);
