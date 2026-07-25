using System.ComponentModel.DataAnnotations;

namespace IdentityService.Options;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, int.MaxValue)]
    public int LoginPermitLimit { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int LoginWindowSeconds { get; set; } = 300;

    [Range(1, int.MaxValue)]
    public int RegisterPermitLimit { get; set; } = 5;

    [Range(1, int.MaxValue)]
    public int RegisterWindowSeconds { get; set; } = 3600;

    [Range(1, int.MaxValue)]
    public int ConfirmEmailPermitLimit { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int ConfirmEmailWindowSeconds { get; set; } = 300;

    [Range(1, int.MaxValue)]
    public int ResendVerificationPermitLimit { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int ResendVerificationWindowSeconds { get; set; } = 3600;

    [Range(1, int.MaxValue)]
    public int RequestPasswordResetPermitLimit { get; set; } = 5;

    [Range(1, int.MaxValue)]
    public int RequestPasswordResetWindowSeconds { get; set; } = 3600;

    [Range(1, int.MaxValue)]
    public int ResetPasswordPermitLimit { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int ResetPasswordWindowSeconds { get; set; } = 300;
}
