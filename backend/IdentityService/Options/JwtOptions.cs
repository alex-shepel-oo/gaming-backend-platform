using System.ComponentModel.DataAnnotations;

namespace IdentityService.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "gaming-backend-platform/identity";

    [Required, MinLength(1)]
    public string[] Audiences { get; set; } = ["gbp-player", "gbp-admin"];

    [Required]
    [MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    [Range(1, int.MaxValue)]
    public int ClockSkewSeconds { get; set; } = 30;
}
