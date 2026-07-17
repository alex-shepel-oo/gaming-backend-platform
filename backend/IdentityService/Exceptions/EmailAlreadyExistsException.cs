namespace IdentityService.Exceptions;

public sealed class EmailAlreadyExistsException() : Exception("An account with this email already exists.");
