using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record ConfirmEmailRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required, RegularExpression(@"^\d{6}$")] string Code);
