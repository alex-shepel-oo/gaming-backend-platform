using System.Text.Json;
using EconomyService.Domain;
using EconomyService.Messaging;
using EconomyService.Persistence;

namespace EconomyService.Services;

public sealed class OutboxWriter(EconomyDbContext dbContext) : IOutboxWriter
{
    public Task WriteAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = integrationEvent.Type,
            Version = integrationEvent.Version,
            Payload = JsonSerializer.Serialize(integrationEvent),
            OccurredAt = integrationEvent.OccurredAt,
            ProcessedAt = null,
            Attempts = 0,
        });

        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
