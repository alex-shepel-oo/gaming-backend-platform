using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record LogoutRequest([property: Required] string RefreshToken);
