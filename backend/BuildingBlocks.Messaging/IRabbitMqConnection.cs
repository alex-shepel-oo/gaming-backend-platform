using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

public interface IRabbitMqConnection
{
    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);

    Task<IConnection> GetConnectionAsync();
}
