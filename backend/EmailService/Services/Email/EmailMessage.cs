namespace EmailService.Services.Email;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);
