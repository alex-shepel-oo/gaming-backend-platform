using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace EmailService.Options;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string Provider { get; set; } = "Smtp";

    [Required]
    [EmailAddress]
    public string From { get; set; } = string.Empty;

    [Required]
    public string FromDisplayName { get; set; } = string.Empty;

    // Directory EmailTemplateRenderer reads the three templates from, fresh, on every render: no
    // caching, so a change here reaches a running pod without a restart. Resolved relative to the
    // process's current directory: that's the project folder for a local `dotnet run` (where the
    // real Templates/ folder already lives in source) and /app for the published Docker image
    // (WORKDIR /app), which is exactly the path the email-service-templates ConfigMap volume mount
    // targets in infra/helm/gaming-backend-platform/values.yaml.
    [Required]
    public string TemplatesPath { get; set; } = "Templates";

    [Required]
    [ValidateObjectMembers]
    public EmailSmtpOptions Smtp { get; set; } = new();
}

public sealed class EmailSmtpOptions
{
    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public string? UserName { get; set; }

    public string? Password { get; set; }
}
