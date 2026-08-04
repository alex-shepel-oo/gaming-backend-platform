using System.Diagnostics;
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
            // This write runs inside the original HTTP request's own scope, so
            // Activity.Current is still the live, correctly-parented request
            // activity - the dispatcher's poll loop has no such ambient
            // context by the time it eventually publishes this row, so the
            // W3C id has to be captured here and carried on the row itself.
            TraceParent = Activity.Current?.Id,
        });

        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
