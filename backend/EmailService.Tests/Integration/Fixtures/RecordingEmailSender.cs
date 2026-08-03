using System.Collections.Concurrent;
using EmailService.Services.Email;

namespace EmailService.Tests.Integration.Fixtures;

public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);

        return Task.CompletedTask;
    }
}
