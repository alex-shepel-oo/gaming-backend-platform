namespace EconomyService.Inbox;

// The consumer's side effect: a per-event-type counter, not a read model.
// It exists so the deduplicating consumer demonstrably does something with
// each message it accepts, and so a test can observe that the something
// happened exactly once per message rather than reading a log line.
public sealed class ProjectedEventCount
{
    public required string EventType { get; init; }
    public required int Count { get; set; }
}
