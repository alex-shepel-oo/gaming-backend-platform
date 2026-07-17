using AwesomeAssertions;
using IdentityService.Options;
using IdentityService.Services.Email;
using Microsoft.Extensions.Options;
using Xunit;

namespace IdentityService.Tests.Unit;

public class SmtpEmailSenderTests
{
    private static readonly EmailOptions Options = new()
    {
        Provider = "Smtp",
        From = "no-reply@gaming-platform.local",
        FromDisplayName = "Gaming Backend Platform",
        Smtp = new EmailSmtpOptions
        {
            Host = "mailpit",
            Port = 1025,
            UseStartTls = false,
        },
    };

    private readonly SmtpEmailSender _sender = new(Microsoft.Extensions.Options.Options.Create(Options));

    [Fact]
    public void BuildMimeMessage_SetsFromToAndSubject()
    {
        var message = new EmailMessage("player@example.com", "Confirm your email", "<p>html</p>", "text");

        var mime = _sender.BuildMimeMessage(message);

        mime.From.Mailboxes.Single().Address.Should().Be(Options.From);
        mime.From.Mailboxes.Single().Name.Should().Be(Options.FromDisplayName);
        mime.To.Mailboxes.Single().Address.Should().Be(message.To);
        mime.Subject.Should().Be(message.Subject);
    }

    [Fact]
    public void BuildMimeMessage_IncludesHtmlAndTextParts()
    {
        var message = new EmailMessage(
            "player@example.com", "Confirm your email", "<p>Your code is 123456</p>", "Your code is 123456");

        var mime = _sender.BuildMimeMessage(message);

        mime.HtmlBody.Should().Be(message.HtmlBody);
        mime.TextBody.Should().Be(message.TextBody);
    }
}
