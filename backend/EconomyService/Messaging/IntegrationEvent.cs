namespace EconomyService.Messaging;

public abstract record IntegrationEvent
{
    public required Guid Id { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }

    public abstract string Type { get; }
    public abstract int Version { get; }
}
