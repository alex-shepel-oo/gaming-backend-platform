using System.Text.Json;
using BuildingBlocks.Messaging;
using EmailService.Messaging.Events;
using EmailService.Services.Email;
using EmailService.Services.Email.Templates;
using Microsoft.Extensions.Options;

namespace EmailService.Messaging;

public sealed class DuplicateRegistrationNoticeRequestedConsumer(
    IRabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IEmailTemplateRenderer templateRenderer,
    IEmailSender emailSender,
    ILogger<DuplicateRegistrationNoticeRequestedConsumer> logger,
    string queueName = "gbp.email.duplicate-registration-notice-requested")
    : QueueConsumerBase(connection, options, logger, queueName, RoutingKey)
{
    private const string RoutingKey = "duplicate_registration_notice.requested";

    protected override async Task SendAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<DuplicateRegistrationNoticeRequestedPayload>(body.Span);
        if (payload is null)
        {
            return;
        }

        var htmlBody = templateRenderer.RenderDuplicateRegistrationNotice(payload.GameName);
        var textBody = templateRenderer.RenderDuplicateRegistrationNoticeText(payload.GameName);

        await emailSender.SendAsync(new EmailMessage(payload.Email, "Registration attempt on your email address", htmlBody, textBody), cancellationToken);
    }
}
