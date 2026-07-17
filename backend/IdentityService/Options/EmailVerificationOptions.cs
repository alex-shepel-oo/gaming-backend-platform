using System.ComponentModel.DataAnnotations;

namespace IdentityService.Options;

public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";

    [Range(1, int.MaxValue)]
    public int CodeTtlMinutes { get; set; } = 20;

    [Range(1, int.MaxValue)]
    public int MaxAttempts { get; set; } = 5;

    [Range(1, int.MaxValue)]
    public int ResendCooldownSeconds { get; set; } = 60;

    [Range(1, int.MaxValue)]
    public int MaxResendsPerHour { get; set; } = 5;
}
