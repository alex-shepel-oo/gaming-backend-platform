namespace IdentityService.Exceptions;

public sealed class InvalidRefreshTokenException() : Exception("Refresh token is invalid, expired, or revoked.");
