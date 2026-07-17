using System.Globalization;
using System.Reflection;

namespace IdentityService.Services.Email.Templates;

public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private const string ResourceName = "IdentityService.Services.Email.Templates.EmailVerification.html";

    private static readonly string EmailVerificationTemplate = LoadTemplate(ResourceName);

    public string RenderEmailVerification(string code, string gameName, int expiresInMinutes) =>
        EmailVerificationTemplate
            .Replace("{{Code}}", code)
            .Replace("{{GameName}}", gameName)
            .Replace("{{ExpiresInMinutes}}", expiresInMinutes.ToString(CultureInfo.InvariantCulture));

    private static string LoadTemplate(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
