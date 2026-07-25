namespace IdentityService.Services.Email.Templates;

public interface IEmailTemplateRenderer
{
    string RenderEmailVerification(string code, string gameName, int expiresInMinutes);

    string RenderPasswordReset(string resetLink, int expiresInMinutes);
}
