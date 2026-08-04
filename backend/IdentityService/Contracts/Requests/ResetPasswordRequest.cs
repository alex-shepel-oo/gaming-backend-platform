using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record ResetPasswordRequest(
    [property: Required] string Token,
    [property: Required, StringLength(128, MinimumLength = 12)] string NewPassword);
