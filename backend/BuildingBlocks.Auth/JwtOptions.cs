using System.ComponentModel.DataAnnotations;

namespace BuildingBlocks.Auth;

// Shared by every service that validates tokens against Identity's published JWKS
// (ApiGateway, EconomyService, NotificationService) - not IdentityService itself, which
// signs with a private key and publishes JWKS rather than fetching it, and so has its own,
// differently-shaped options class.
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
