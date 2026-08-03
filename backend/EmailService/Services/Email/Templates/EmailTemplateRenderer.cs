using System.Globalization;
using EmailService.Options;
using Microsoft.Extensions.Options;

namespace EmailService.Services.Email.Templates;

// Unlike IdentityService's old EmailTemplateRenderer (an EmbeddedResource loaded once into a
// `static readonly string` at type-init), this one re-reads the file off disk on every render call.
// That is the entire point of moving the templates out of the assembly: a ConfigMap-mounted file
// (email-service-templates in infra/helm/gaming-backend-platform/values.yaml) changes on disk
// underneath a running pod without a restart, and only a renderer that never caches actually
// notices. The three files are small HTML documents, so paying a File.ReadAllText per send is cheap
// next to the SMTP round trip that follows it.
public sealed class EmailTemplateRenderer(IOptions<EmailOptions> options) : IEmailTemplateRenderer
{
    private const string EmailVerificationFileName = "EmailVerification.html";
    private const string PasswordResetFileName = "PasswordReset.html";
    private const string DuplicateRegistrationNoticeFileName = "DuplicateRegistrationNotice.html";

    private readonly EmailOptions _options = options.Value;

    public string RenderEmailVerification(string code, string gameName, int expiresInMinutes) =>
        ReadTemplate(EmailVerificationFileName)
            .Replace("{{Code}}", code)
            .Replace("{{GameName}}", gameName)
            .Replace("{{ExpiresInMinutes}}", expiresInMinutes.ToString(CultureInfo.InvariantCulture));

    public string RenderPasswordReset(string resetLink, int expiresInMinutes) =>
        ReadTemplate(PasswordResetFileName)
            .Replace("{{ResetLink}}", resetLink)
            .Replace("{{ExpiresInMinutes}}", expiresInMinutes.ToString(CultureInfo.InvariantCulture));

    public string RenderDuplicateRegistrationNotice(string gameName) =>
        ReadTemplate(DuplicateRegistrationNoticeFileName)
            .Replace("{{GameName}}", gameName);

    private string ReadTemplate(string fileName) =>
        File.ReadAllText(Path.Combine(_options.TemplatesPath, fileName));
}
