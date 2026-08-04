namespace IdentityService.Exceptions;

public sealed class PermissionEscalationException()
    : Exception("The caller cannot grant permissions outside their own scope or possession.");
