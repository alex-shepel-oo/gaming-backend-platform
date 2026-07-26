using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts.Requests;

public sealed record SelectGameRequest([property: Required] Guid GameId);
