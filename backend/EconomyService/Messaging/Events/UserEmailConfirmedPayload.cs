namespace EconomyService.Messaging.Events;

// Economy's own tolerant view of identity-service's UserEmailConfirmedEvent -
// not a shared type imported from Identity. Id is required because
// InboxConsumerBase already reads it off the raw JSON for the dedup key
// before this ever runs; this record just needs to deserialize the same
// payload, ignoring the fields it doesn't care about (OccurredAt).
public sealed record UserEmailConfirmedPayload
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
}
