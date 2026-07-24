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
}
