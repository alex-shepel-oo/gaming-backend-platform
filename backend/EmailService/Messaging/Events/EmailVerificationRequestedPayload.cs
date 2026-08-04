namespace EmailService.Messaging.Events;

// EmailService's own tolerant view of identity-service's EmailVerificationRequestedEvent -- not a
// shared type imported from Identity, the same convention EconomyService's UserEmailConfirmedPayload
// already established for reading another service's outbox event off the wire.
public sealed record EmailVerificationRequestedPayload
{
    public required string Email { get; init; }
    public required string Code { get; init; }
    public required string GameName { get; init; }
    public required int ExpiresInMinutes { get; init; }
}
