using System.Text.Json;
using EconomyService.Inbox;
using EconomyService.Options;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EconomyService.Messaging;

// The first real consumer in the system: everything before this only ever
// published. It binds its own queue to a subset of the topic exchange's
// routing keys - the outbox dispatcher never declared one, since a
// producer-only exchange doesn't need a queue behind it (A.3).
//
// This is self-consumption for demonstration, not a production subscriber:
// EconomyService both emits BalanceChanged/conversion.* events through the
// outbox and, here, consumes them back off the same exchange to show the
// delivery loop (outbox -> broker -> consumer) surviving redelivery. A real
// external subscriber is a later slice's concern.
public sealed partial class DeduplicatingEventConsumer(
    IRabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    IInboxFaultInjector faultInjector,
    TimeProvider timeProvider,
    ILogger<DeduplicatingEventConsumer> logger,
    // Not a service default a deployer would ever want to change - just a
    // seam so integration tests can each bind their own queue instead of
    // sharing one durable queue across test runs.
    string queueName = "gbp.economy.log-projector") : BackgroundService
{
    private static readonly string[] RoutingKeys =
    [
        "balance.changed",
        "conversion.debited",
        "conversion.completed",
        "conversion.failed",
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(stoppingToken);

        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);

        foreach (var routingKey in RoutingKeys)
        {
            await channel.QueueBindAsync(queueName, options.Value.ExchangeName, routingKey, cancellationToken: stoppingToken);
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

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.ProcessedMessages.Add(new ProcessedMessage
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
            // silently losing the projection update.
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO projected_event_counts (event_type, count)
                VALUES ({delivery.RoutingKey}, 1)
                ON CONFLICT (event_type) DO UPDATE SET count = projected_event_counts.count + 1
                """,
                cancellationToken);

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
        LogEventProjected(messageId, delivery.RoutingKey);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not decode a message id from a delivery on routing key {RoutingKey}, acking without processing")]
    private partial void LogUndecodableDelivery(Exception exception, string routingKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping message {MessageId} on routing key {RoutingKey}: already processed")]
    private partial void LogDuplicateSkipped(Guid messageId, string routingKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Processing message {MessageId} on routing key {RoutingKey} failed before commit, message will be redelivered")]
    private partial void LogProcessingFailed(Exception exception, Guid messageId, string routingKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Projected message {MessageId} on routing key {RoutingKey}")]
    private partial void LogEventProjected(Guid messageId, string routingKey);
}
