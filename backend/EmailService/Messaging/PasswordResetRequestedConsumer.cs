using System.Text.Json;
using BuildingBlocks.Messaging;
using EmailService.Messaging.Events;
using EmailService.Services.Email;
using EmailService.Services.Email.Templates;
using Microsoft.Extensions.Options;

namespace EmailService.Messaging;

public sealed class PasswordResetRequestedConsumer(
    IRabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IEmailTemplateRenderer templateRenderer,
    IEmailSender emailSender,
    ILogger<PasswordResetRequestedConsumer> logger,
    string queueName = "gbp.email.password-reset-requested")
    : QueueConsumerBase(connection, options, logger, queueName, RoutingKey)
{
    private const string RoutingKey = "password_reset.requested";

    protected override async Task SendAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<PasswordResetRequestedPayload>(body.Span);
        if (payload is null)
        {
            return;
        }

        var htmlBody = templateRenderer.RenderPasswordReset(payload.ResetLink, payload.ExpiresInMinutes);
        var textBody = templateRenderer.RenderPasswordResetText(payload.ResetLink, payload.ExpiresInMinutes);

        await emailSender.SendAsync(new EmailMessage(payload.Email, "Reset your password", htmlBody, textBody), cancellationToken);
    }
}
