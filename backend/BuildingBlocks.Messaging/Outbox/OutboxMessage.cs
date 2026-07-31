namespace BuildingBlocks.Messaging.Outbox;

public sealed class OutboxMessage
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }
    public required int Version { get; init; }
    public required string Payload { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }

    // The W3C traceparent string captured off Activity.Current at write time (OutboxWriter),
    // read back by the dispatcher to re-parent the eventual publish onto the request that
    // originally wrote this row. Null for rows written before this column existed, or by any
    // caller with no live Activity at write time - the dispatcher treats that as "start a fresh
    // root" rather than an error.
    public string? TraceParent { get; init; }
}
