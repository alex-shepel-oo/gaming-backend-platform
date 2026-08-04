using System.Text.Json;
using BuildingBlocks.Messaging.Tracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BuildingBlocks.Messaging.Inbox;

// The generic half of a deduplicating consumer: connect, bind, receive, dedup
// transaction, ack/nack. A concrete subclass supplies the queue name, the
// routing keys it binds to, and what to actually do with an accepted message
// - everything else is the same RabbitMQ + inbox contour regardless of which
// service or which side effect is behind it.
public abstract partial class InboxConsumerBase<TDbContext>(
    IRabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IInboxFaultInjector faultInjector,
    TimeProvider timeProvider,
    ILogger logger,
    string exchangeName,
    string queueName,
    IReadOnlyList<string> routingKeys) : BackgroundService
    where TDbContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(stoppingToken);

        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);

        foreach (var routingKey in routingKeys)
        {
            await channel.QueueBindAsync(queueName, exchangeName, routingKey, cancellationToken: stoppingToken);
        }

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
        Guid messageId;

        try
        {
            using var payload = JsonDocument.Parse(delivery.Body.ToArray());
            messageId = payload.RootElement.GetProperty("Id").GetGuid();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not a message the dedup key can be recovered from at all - no
            // amount of redelivery fixes that. Ack it away rather than
            // looping on a message that can never succeed (no DLQ in this
            // slice, same accepted gap as the outbox dispatcher's).
            LogUndecodableDelivery(ex, delivery.RoutingKey);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            return;
        }

        // Parented to whatever traceparent/tracestate rode along on the delivery's AMQP headers
        // (the producer side of BuildingBlocks.Messaging.Tracing set these); a delivery with none -
        // published before this session, or from a producer that never captured a trace - roots a
        // fresh activity here rather than throwing, so the dedup/side-effect flow below is unaffected.
        using var activity = MessagingTracePropagation.StartConsumerActivity($"{delivery.RoutingKey} process", delivery.BasicProperties.Headers);

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Set<ProcessedMessage>().Add(new ProcessedMessage
        {
            MessageId = messageId,
            ProcessedAt = timeProvider.GetUtcNow(),
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The PK conflict itself is the dedup signal: some earlier
            // delivery of this same at-least-once message already got here
            // and committed. Nothing left to redo.
            await transaction.RollbackAsync(cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            LogDuplicateSkipped(messageId, delivery.RoutingKey);
            return;
        }

        try
        {
            // Side effect and the processed_messages insert above share this
            // one transaction on purpose: a crash after this line and before
            // the commit below rolls both back together, so redelivery finds
            // no processed_messages row and reprocesses cleanly instead of
            // silently losing the side effect.
            await ApplySideEffectAsync(dbContext, messageId, delivery.RoutingKey, delivery.Body, cancellationToken);

            await faultInjector.BeforeCommitAsync(messageId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProcessingFailed(ex, messageId, delivery.RoutingKey);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
            return;
        }

        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        LogSideEffectApplied(messageId, delivery.RoutingKey);
    }

    protected abstract Task ApplySideEffectAsync(
        TDbContext dbContext, Guid messageId, string routingKey, ReadOnlyMemory<byte> body, CancellationToken cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not decode a message id from a delivery on routing key {RoutingKey}, acking without processing")]
    private partial void LogUndecodableDelivery(Exception exception, string routingKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping message {MessageId} on routing key {RoutingKey}: already processed")]
    private partial void LogDuplicateSkipped(Guid messageId, string routingKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Processing message {MessageId} on routing key {RoutingKey} failed before commit, message will be redelivered")]
    private partial void LogProcessingFailed(Exception exception, Guid messageId, string routingKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied side effect for message {MessageId} on routing key {RoutingKey}")]
    private partial void LogSideEffectApplied(Guid messageId, string routingKey);
}
