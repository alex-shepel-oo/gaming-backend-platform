using RabbitMQ.Client;

namespace EconomyService.Messaging;

public interface IRabbitMqConnection
{
    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);

    Task<IConnection> GetConnectionAsync();
}
