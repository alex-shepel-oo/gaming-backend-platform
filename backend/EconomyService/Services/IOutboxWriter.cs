using EconomyService.Messaging;

namespace EconomyService.Services;

public interface IOutboxWriter
{
    Task WriteAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent;
}
