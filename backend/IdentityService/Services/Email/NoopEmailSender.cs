using Microsoft.Extensions.Logging;

namespace IdentityService.Services.Email;

public sealed partial class NoopEmailSender(ILogger<NoopEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        LogEmailNotSent(message.To, message.Subject);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Email to {To} with subject \"{Subject}\" was not sent (Noop provider)")]
    private partial void LogEmailNotSent(string to, string subject);
}
