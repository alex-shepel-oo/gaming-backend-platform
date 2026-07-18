using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record RefreshRequest([property: Required] string RefreshToken);
