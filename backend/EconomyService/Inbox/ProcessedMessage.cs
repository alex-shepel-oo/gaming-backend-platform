namespace EconomyService.Inbox;

// A dedup ledger, not a full inbox: one row per message id the consumer has
// already handled. The consumer checks for a row before doing any work, and
// the PK itself is what turns a redelivered at-least-once message into a
// no-op (ADR-0010 addendum).
public sealed class ProcessedMessage
{
    public required Guid MessageId { get; init; }
    public required DateTimeOffset ProcessedAt { get; init; }
}
