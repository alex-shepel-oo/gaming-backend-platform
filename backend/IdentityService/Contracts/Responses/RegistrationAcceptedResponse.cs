namespace IdentityService.Contracts.Responses;

public sealed record RegistrationAcceptedResponse(
    Guid UserId,
    string Email,
    bool VerificationRequired,
    DateTimeOffset? CodeExpiresAt);
