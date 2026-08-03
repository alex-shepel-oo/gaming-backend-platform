using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Tracing;
using EmailService.Messaging.Events;
using EmailService.Services.Email;
using EmailService.Services.Email.Templates;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EmailService.Messaging;

// Not InboxConsumerBase<TDbContext>: that base class requires a DbContext for its dedup
// transaction, and EmailService has no database by design (same reasoning
// NotificationService's BalanceChangedConsumer already states for itself). Hand-rolled
// BackgroundService straight on IRabbitMqConnection instead, with no dedup step at all -- a
// redelivered message just sends the same verification email again, which is a deliberate
// accepted trade-off (see the brief), not an oversight: dedup would mean giving EmailService a
// database, the exact complexity this extraction exists to avoid.
public sealed partial class EmailVerificationRequestedConsumer(
    IRabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IEmailTemplateRenderer templateRenderer,
    IEmailSender emailSender,
    ILogger<EmailVerificationRequestedConsumer> logger,
    // Not a service default a deployer would ever want to change -- just a seam so integration
    // tests can each bind their own queue instead of sharing one durable queue across test runs.
    string queueName = "gbp.email.verification-requested") : BackgroundService
{
    private const string RoutingKey = "email_verification.requested";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(stoppingToken);

        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(queueName, options.Value.ExchangeName, RoutingKey, cancellationToken: stoppingToken);

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
            var payload = JsonSerializer.Deserialize<EmailVerificationRequestedPayload>(delivery.Body.Span);
            if (payload is not null)
            {
                var htmlBody = templateRenderer.RenderEmailVerification(payload.Code, payload.GameName, payload.ExpiresInMinutes);
                var textBody =
                    $"Confirm your email for {payload.GameName}. Your verification code is {payload.Code}. " +
                    $"It expires in {payload.ExpiresInMinutes} minutes.";

                await emailSender.SendAsync(
                    new EmailMessage(payload.Email, "Confirm your email", htmlBody, textBody),
                    cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No DLQ, no retry loop -- a delivery that can't be decoded or sent isn't fixed by
            // seeing it again immediately. Ack it away and log, same accepted gap as the outbox
            // dispatcher's and BalanceChangedConsumer's.
            LogDeliveryFailed(ex, delivery.RoutingKey);
        }

        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process delivery on routing key {RoutingKey}, acking without retry")]
    private partial void LogDeliveryFailed(Exception exception, string routingKey);
}
