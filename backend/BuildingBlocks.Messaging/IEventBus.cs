namespace BuildingBlocks.Messaging;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent;

    // Used by the outbox dispatcher, which only has the type/version/payload
    // already persisted in an outbox row, not the original typed event.
    // headers carries the W3C traceparent/tracestate pair the dispatcher
    // injects for the row's re-parented Producer activity - optional (and
    // AMQP-agnostic: plain string headers, not a BasicProperties) so callers
    // that publish directly, without a trace to propagate, don't need to know
    // this exists.
    Task PublishAsync(
        EventEnvelope envelope,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}
