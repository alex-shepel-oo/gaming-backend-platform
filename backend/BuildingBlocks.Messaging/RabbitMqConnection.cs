using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

public sealed class RabbitMqConnection : IRabbitMqConnection, IAsyncDisposable
{
    private readonly Lazy<Task<IConnection>> _connection;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options)
    {
        var rabbitMqOptions = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = rabbitMqOptions.Host,
            Port = rabbitMqOptions.Port,
            UserName = rabbitMqOptions.Username,
            Password = rabbitMqOptions.Password,
            VirtualHost = rabbitMqOptions.VirtualHost,
        };

        _connection = new Lazy<Task<IConnection>>(() => factory.CreateConnectionAsync());
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connection.Value.WaitAsync(cancellationToken);
        return await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    public Task<IConnection> GetConnectionAsync() => _connection.Value;

    public async ValueTask DisposeAsync()
    {
        if (!_connection.IsValueCreated)
        {
            return;
        }

        var connection = await _connection.Value;
        await connection.DisposeAsync();
    }
}
