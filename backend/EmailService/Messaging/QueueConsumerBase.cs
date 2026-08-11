using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Tracing;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EmailService.Messaging;

// Shared connect/declare/bind/consume/ack loop for EmailService's queue consumers. No dedup, no
// DbContext, so this isn't InboxConsumerBase<TDbContext> - EmailService has neither by design (see
// ADR-0024): a redelivered message just sends the same email again, an accepted trade-off rather
// than something worth giving this service a database to dedup against. A subclass supplies the
// queue name, the routing key it binds to, and how to turn a delivery's body into a sent email.
public abstract partial class QueueConsumerBase(
    IRabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    ILogger logger,
    string queueName,
    string routingKey) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(stoppingToken);

        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(queueName, options.Value.ExchangeName, routingKey, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, delivery, stoppingToken);

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer, stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        using var activity = MessagingTracePropagation.StartConsumerActivity($"{delivery.RoutingKey} process", delivery.BasicProperties.Headers);

        try
        {
            await SendAsync(delivery.Body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDeliveryFailed(ex, delivery.RoutingKey);
        }

        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
    }

    // No DLQ, no retry loop - same accepted gap as the outbox dispatcher's and
    // BalanceChangedConsumer's. Implementations should return quietly (not throw) for a delivery
    // that fails to decode, since that isn't fixed by seeing it again.
    protected abstract Task SendAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process delivery on routing key {RoutingKey}, acking without retry")]
    private partial void LogDeliveryFailed(Exception exception, string routingKey);
}
