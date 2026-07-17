namespace IdentityService.Exceptions;

public sealed class RefreshTokenOwnerMismatchException() : Exception("This refresh token belongs to a different account.");
