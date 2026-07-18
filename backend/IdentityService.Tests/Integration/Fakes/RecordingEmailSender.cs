using System.Collections.Concurrent;
using IdentityService.Services.Email;

namespace IdentityService.Tests.Integration.Fakes;

public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);

        return Task.CompletedTask;
    }

    public void Clear() => _sent.Clear();
}
