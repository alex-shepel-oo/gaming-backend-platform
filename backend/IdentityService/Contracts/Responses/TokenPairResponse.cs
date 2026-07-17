namespace IdentityService.Contracts.Responses;

public sealed record TokenPairResponse(string AccessToken, string RefreshToken);
