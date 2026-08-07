namespace EmailService.Services.Email.Templates;

public interface IEmailTemplateRenderer
{
    string RenderEmailVerification(string code, string gameName, int expiresInMinutes);

    string RenderEmailVerificationText(string code, string gameName, int expiresInMinutes);

    string RenderPasswordReset(string resetLink, int expiresInMinutes);

    string RenderPasswordResetText(string resetLink, int expiresInMinutes);

    string RenderDuplicateRegistrationNotice(string gameName);

    string RenderDuplicateRegistrationNoticeText(string gameName);
}
