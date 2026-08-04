using System.ComponentModel.DataAnnotations;

namespace IdentityService.Options;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    [Range(1, int.MaxValue)]
    public int TokenTtlMinutes { get; set; } = 30;

    [Range(1, int.MaxValue)]
    public int CooldownSeconds { get; set; } = 60;
}
