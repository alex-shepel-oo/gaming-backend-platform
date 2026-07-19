using System.ComponentModel.DataAnnotations;

namespace EconomyService.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "gaming-backend-platform/identity";

    [Required]
    public string Audience { get; set; } = "gaming-backend-platform";

    [Required]
    [MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int ClockSkewSeconds { get; set; } = 30;
}
