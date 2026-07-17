namespace IdentityService.Exceptions;

public sealed class EmailNotConfirmedException() : Exception("This account's email address has not been confirmed yet.");
