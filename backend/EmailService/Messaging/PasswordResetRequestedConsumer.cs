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

// Same hand-rolled shape as EmailVerificationRequestedConsumer -- see that class's own comment for
// why this isn't InboxConsumerBase<TDbContext> and why redelivery-caused duplicate sends are an
// accepted trade-off here rather than something dedup'd against.
public sealed partial class PasswordResetRequestedConsumer(
    IRabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IEmailTemplateRenderer templateRenderer,
    IEmailSender emailSender,
    ILogger<PasswordResetRequestedConsumer> logger,
    string queueName = "gbp.email.password-reset-requested") : BackgroundService
{
    private const string RoutingKey = "password_reset.requested";

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
            var payload = JsonSerializer.Deserialize<PasswordResetRequestedPayload>(delivery.Body.Span);
            if (payload is not null)
            {
                var htmlBody = templateRenderer.RenderPasswordReset(payload.ResetLink, payload.ExpiresInMinutes);
                var textBody =
                    $"We received a request to reset your password. Use this link: {payload.ResetLink}. " +
                    $"It expires in {payload.ExpiresInMinutes} minutes.";

                await emailSender.SendAsync(
                    new EmailMessage(payload.Email, "Reset your password", htmlBody, textBody),
                    cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDeliveryFailed(ex, delivery.RoutingKey);
        }

        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process delivery on routing key {RoutingKey}, acking without retry")]
    private partial void LogDeliveryFailed(Exception exception, string routingKey);
}
