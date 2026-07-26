namespace IdentityService.Exceptions;

public sealed class InvalidAvatarUrlException() : Exception("Avatar URL must be a valid http or https URL.");
