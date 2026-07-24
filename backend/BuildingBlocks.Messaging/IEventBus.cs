namespace BuildingBlocks.Messaging;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent;

    // Used by the outbox dispatcher, which only has the type/version/payload
    // already persisted in an outbox row, not the original typed event.
    Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken = default);
}
