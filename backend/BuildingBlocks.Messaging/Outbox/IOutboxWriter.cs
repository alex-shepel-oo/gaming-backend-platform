namespace BuildingBlocks.Messaging.Outbox;

public interface IOutboxWriter
{
    Task WriteAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent;
}
