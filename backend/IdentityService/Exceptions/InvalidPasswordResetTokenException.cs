namespace IdentityService.Exceptions;

public sealed class InvalidPasswordResetTokenException() : Exception("The password reset token is invalid, expired, or already used.");
