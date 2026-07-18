using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace IdentityService.Options;

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
