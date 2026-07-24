namespace IdentityService.Contracts.Requests;

public sealed record UpdateGameRequest(string? Name, bool? IsActive);
