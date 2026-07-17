namespace IdentityService.Exceptions;

public sealed class NoAccessToGameException() : Exception("This account has no role in the requested game.");
