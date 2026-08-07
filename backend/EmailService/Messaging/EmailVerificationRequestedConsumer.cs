using System.Text.Json;
using BuildingBlocks.Messaging;
using EmailService.Messaging.Events;
using EmailService.Services.Email;
using EmailService.Services.Email.Templates;
using Microsoft.Extensions.Options;

namespace EmailService.Messaging;

public sealed class EmailVerificationRequestedConsumer(
    IRabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IEmailTemplateRenderer templateRenderer,
    IEmailSender emailSender,
    ILogger<EmailVerificationRequestedConsumer> logger,
    // Not a service default a deployer would ever want to change: just a seam so integration
    // tests can each bind their own queue instead of sharing one durable queue across test runs.
    string queueName = "gbp.email.verification-requested")
    : QueueConsumerBase(connection, options, logger, queueName, RoutingKey)
{
    private const string RoutingKey = "email_verification.requested";

    protected override async Task SendAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<EmailVerificationRequestedPayload>(body.Span);
        if (payload is null)
        {
            return;
        }

        var htmlBody = templateRenderer.RenderEmailVerification(payload.Code, payload.GameName, payload.ExpiresInMinutes);
        var textBody = templateRenderer.RenderEmailVerificationText(payload.Code, payload.GameName, payload.ExpiresInMinutes);

        await emailSender.SendAsync(new EmailMessage(payload.Email, "Confirm your email", htmlBody, textBody), cancellationToken);
    }
}
