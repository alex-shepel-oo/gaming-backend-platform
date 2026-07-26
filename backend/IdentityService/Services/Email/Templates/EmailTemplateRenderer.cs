using System.Globalization;
using System.Reflection;

namespace IdentityService.Services.Email.Templates;

public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private const string ResourceName = "IdentityService.Services.Email.Templates.EmailVerification.html";
    private const string PasswordResetResourceName = "IdentityService.Services.Email.Templates.PasswordReset.html";
    private const string DuplicateRegistrationNoticeResourceName =
        "IdentityService.Services.Email.Templates.DuplicateRegistrationNotice.html";

    private static readonly string EmailVerificationTemplate = LoadTemplate(ResourceName);
    private static readonly string PasswordResetTemplate = LoadTemplate(PasswordResetResourceName);
    private static readonly string DuplicateRegistrationNoticeTemplate = LoadTemplate(DuplicateRegistrationNoticeResourceName);

    public string RenderEmailVerification(string code, string gameName, int expiresInMinutes) =>
        EmailVerificationTemplate
            .Replace("{{Code}}", code)
            .Replace("{{GameName}}", gameName)
            .Replace("{{ExpiresInMinutes}}", expiresInMinutes.ToString(CultureInfo.InvariantCulture));

    public string RenderPasswordReset(string resetLink, int expiresInMinutes) =>
        PasswordResetTemplate
            .Replace("{{ResetLink}}", resetLink)
            .Replace("{{ExpiresInMinutes}}", expiresInMinutes.ToString(CultureInfo.InvariantCulture));

    public string RenderDuplicateRegistrationNotice(string gameName) =>
        DuplicateRegistrationNoticeTemplate
            .Replace("{{GameName}}", gameName);

    private static string LoadTemplate(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
