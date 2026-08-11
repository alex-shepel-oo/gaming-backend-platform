using AwesomeAssertions;
using EmailService.Options;
using EmailService.Services.Email.Templates;
using Xunit;

namespace EmailService.Tests.Unit;

// The whole point of moving these templates out of EmbeddedResource (see EmailTemplateRenderer's own
// comment) is that a ConfigMap-mounted file change reaches a running pod without a restart. That
// only holds if the renderer genuinely re-reads the file on every call instead of caching it once --
// this is the one behavior the brief calls out as needing its own explicit test, not an implicit
// assumption, so it gets proven directly against the filesystem here rather than through the full
// RabbitMQ/consumer pipeline.
public sealed class EmailTemplateRendererTests : IDisposable
{
    private readonly string _templatesDirectory = Directory.CreateTempSubdirectory("email-template-renderer-tests-").FullName;

    public void Dispose() => Directory.Delete(_templatesDirectory, recursive: true);

    [Fact]
    public void RenderEmailVerification_FillsCodeGameNameAndExpiry()
    {
        WriteTemplate("EmailVerification.html", "<p>{{GameName}}</p><p>{{Code}}</p><p>{{ExpiresInMinutes}}</p>");
        var renderer = CreateRenderer();

        var rendered = renderer.RenderEmailVerification("123456", "Test Game", 20);

        rendered.Should().Contain("Test Game").And.Contain("123456").And.Contain("20");
    }

    [Fact]
    public void RenderPasswordReset_FillsResetLinkAndExpiry()
    {
        WriteTemplate("PasswordReset.html", "<a href=\"{{ResetLink}}\">link</a><p>{{ExpiresInMinutes}}</p>");
        var renderer = CreateRenderer();

        var rendered = renderer.RenderPasswordReset("http://localhost:8080/reset-password?token=abc", 30);

        rendered.Should().Contain("http://localhost:8080/reset-password?token=abc").And.Contain("30");
    }

    [Fact]
    public void RenderDuplicateRegistrationNotice_FillsGameName()
    {
        WriteTemplate("DuplicateRegistrationNotice.html", "<p>{{GameName}}</p>");
        var renderer = CreateRenderer();

        var rendered = renderer.RenderDuplicateRegistrationNotice("Test Game");

        rendered.Should().Contain("Test Game");
    }

    [Fact]
    public void RenderEmailVerificationText_FillsCodeGameNameAndExpiry()
    {
        WriteTemplate("EmailVerification.txt", "{{GameName}} {{Code}} {{ExpiresInMinutes}}");
        var renderer = CreateRenderer();

        var rendered = renderer.RenderEmailVerificationText("123456", "Test Game", 20);

        rendered.Should().Contain("Test Game").And.Contain("123456").And.Contain("20");
    }

    [Fact]
    public void RenderPasswordResetText_FillsResetLinkAndExpiry()
    {
        WriteTemplate("PasswordReset.txt", "{{ResetLink}} {{ExpiresInMinutes}}");
        var renderer = CreateRenderer();

        var rendered = renderer.RenderPasswordResetText("http://localhost:8080/reset-password?token=abc", 30);

        rendered.Should().Contain("http://localhost:8080/reset-password?token=abc").And.Contain("30");
    }

    [Fact]
    public void RenderDuplicateRegistrationNoticeText_FillsGameName()
    {
        WriteTemplate("DuplicateRegistrationNotice.txt", "{{GameName}}");
        var renderer = CreateRenderer();

        var rendered = renderer.RenderDuplicateRegistrationNoticeText("Test Game");

        rendered.Should().Contain("Test Game");
    }

    [Fact]
    public void RenderEmailVerification_TemplateFileEditedAfterFirstRender_NextRenderReflectsTheEditWithoutAnyRestart()
    {
        WriteTemplate("EmailVerification.html", "<p>original {{Code}}</p>");
        var renderer = CreateRenderer();

        var firstRender = renderer.RenderEmailVerification("111111", "Test Game", 20);
        firstRender.Should().Contain("original");

        // Same file path, same renderer instance, no process restart, no new IEmailTemplateRenderer:
        // just the file on disk changing underneath it, exactly what a `helm upgrade`/`kubectl
        // apply` against the email-service-templates ConfigMap does to a running pod's mounted file.
        WriteTemplate("EmailVerification.html", "<p>updated {{Code}}</p>");

        var secondRender = renderer.RenderEmailVerification("222222", "Test Game", 20);

        secondRender.Should().Contain("updated").And.Contain("222222");
        secondRender.Should().NotContain("original");
    }

    private void WriteTemplate(string fileName, string content) =>
        File.WriteAllText(Path.Combine(_templatesDirectory, fileName), content);

    private EmailTemplateRenderer CreateRenderer() =>
        new(Microsoft.Extensions.Options.Options.Create(new EmailOptions { TemplatesPath = _templatesDirectory }));
}
