using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Tracing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using NotificationService.Hubs;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationService.Messaging;

// Not InboxConsumerBase<TDbContext>: that base class requires a DbContext for
// its dedup transaction, and this service has no database by design.
// Hand-rolled BackgroundService straight on IRabbitMqConnection instead, with
// no dedup step at all -- a redelivered message just pushes the same (still
// current) balance again, which a client harmlessly re-renders.
public sealed partial class BalanceChangedConsumer(
    IRabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IHubContext<NotificationHub> hubContext,
    ILogger<BalanceChangedConsumer> logger) : BackgroundService
{
    private const string QueueName = "gbp.notification.balance-changed";
    private const string RoutingKey = "balance.changed";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(stoppingToken);

        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(QueueName, options.Value.ExchangeName, RoutingKey, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, delivery, stoppingToken);

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken);

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
        // Same extract-and-parent step InboxConsumerBase uses, factored into BuildingBlocks.Messaging
        // rather than duplicated here - this consumer has no DbContext to be generic over (see the
        // type-level comment above) so it can't share the base class itself, only this helper.
        using var activity = MessagingTracePropagation.StartConsumerActivity($"{delivery.RoutingKey} process", delivery.BasicProperties.Headers);

        try
        {
            var notification = JsonSerializer.Deserialize<BalanceChangedNotification>(delivery.Body.Span);
            if (notification is not null)
            {
                // The payload already carries the user id for SignalR routing below - tagging the
                // span with it costs nothing extra and is the whole point of this consumer's trace.
                activity?.SetTag("enduser.id", notification.UserId);

                await hubContext.Clients.User(notification.UserId.ToString())
                    .SendAsync("balanceChanged", new
                    {
                        currencyId = notification.CurrencyId,
                        amount = notification.Amount,
                        balance = notification.Balance,
                    }, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No DLQ, no retry loop for a live push - a delivery that can't be
            // decoded or delivered isn't fixed by seeing it again immediately.
            // Ack it away and log, same accepted gap as the outbox dispatcher's.
            LogDeliveryFailed(ex, delivery.RoutingKey);
        }

        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process delivery on routing key {RoutingKey}, acking without retry")]
    private partial void LogDeliveryFailed(Exception exception, string routingKey);
}
