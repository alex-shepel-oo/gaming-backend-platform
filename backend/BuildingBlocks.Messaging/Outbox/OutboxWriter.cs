using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Messaging.Outbox;

public sealed class OutboxWriter<TDbContext>(TDbContext dbContext) : IOutboxWriter
    where TDbContext : DbContext
{
    public Task WriteAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        dbContext.Set<OutboxMessage>().Add(new OutboxMessage
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
