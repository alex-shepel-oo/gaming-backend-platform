namespace IdentityService.Exceptions;

public sealed class InvalidIconUrlException() : Exception("Icon URL must be a valid http or https URL.");
