using System.ComponentModel.DataAnnotations;

namespace EconomyService.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "gaming-backend-platform/identity";

    [Required, MinLength(1)]
    public string[] Audiences { get; set; } = ["gbp-player", "gbp-admin"];

    [Required]
    public string JwksUri { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int ClockSkewSeconds { get; set; } = 30;
}
