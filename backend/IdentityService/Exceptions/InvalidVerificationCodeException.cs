namespace IdentityService.Exceptions;

public sealed class InvalidVerificationCodeException() : Exception("The verification code is invalid, expired, or already used.");
