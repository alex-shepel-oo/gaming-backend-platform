using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Options;

public sealed class RefreshCookieOptions
{
    public const string SectionName = "Cookie";

    [Required]
    public string Name { get; set; } = "gbp_refresh";

    [Required]
    public string Path { get; set; } = "/api/identity/auth";

    // Not configurable: a refresh cookie a script can read defeats the point of ADR-0011.
    public bool HttpOnly => true;

    public bool RequireSecure { get; set; } = true;

    public SameSiteMode SameSite { get; set; } = SameSiteMode.Strict;

    [Range(1, int.MaxValue)]
    public int MaxAgeDays { get; set; } = 14;
}
