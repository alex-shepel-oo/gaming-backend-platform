using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

public sealed class RabbitMqEventBus(IRabbitMqConnection connection, IOptions<RabbitMqOptions> options) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent =>
        PublishAsync(
            new EventEnvelope(integrationEvent.Type, integrationEvent.Version, JsonSerializer.Serialize(integrationEvent)),
            headers: null,
            cancellationToken);

    public async Task PublishAsync(
        EventEnvelope envelope, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Persistent = true,
        };

        if (headers is { Count: > 0 })
        {
            properties.Headers = headers.ToDictionary(kv => kv.Key, object? (kv) => kv.Value);
        }

        // Routing key = event type: the topic exchange dispatches on exactly
        // this, and a future consumer binds its queue to the subset it cares about.
        await channel.BasicPublishAsync(
            exchange: options.Value.ExchangeName,
            routingKey: envelope.Type,
            mandatory: false,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(envelope.Payload),
            cancellationToken: cancellationToken);
    }
}
